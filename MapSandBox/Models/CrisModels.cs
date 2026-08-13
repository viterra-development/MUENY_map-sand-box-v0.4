using System.Text.Json.Serialization;
using CsvHelper.Configuration.Attributes;

namespace MapSandBox.Models;

// Core CRIS data structures aligned with model card specs
public class CrashRecord
{
    public string CrashId { get; set; } = "";
    public DateTime CrashDateTime { get; set; }
    public string RoadwayId { get; set; } = "";
    public int? Segment { get; set; }
    public KabcoSeverity Severity { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public int? Aadt { get; set; }
    public int? Pci { get; set; }
    public decimal? LidarElevation { get; set; }
    public List<ContributingFactor> ContributingFactors { get; set; } = new();
    public string WeatherCondition { get; set; } = "";
    public string LightCondition { get; set; } = "";
    public string RoadwayCondition { get; set; } = "";
    public List<VehicleInfo> Vehicles { get; set; } = new();
    public List<PersonInfo> Persons { get; set; } = new();
    public int TotalPersons { get; set; }
    public int TotalVehicles { get; set; }
    public int PersonsInvolved { get; set; }
    public int VehiclesInvolved { get; set; }
    public bool IsPrivateProperty { get; set; }
    public bool IsLocated { get; set; }

    // DEM-derived slope information
    public decimal SlopeAtLocation { get; set; }  // Degrees from DEM
    public decimal SlopePercentage { get; set; }  // Percentage grade
    public string SlopeCategory { get; set; } = ""; // Flat/Moderate/Steep
}

public class CrisModelScore
{
    public string LocationId { get; set; } = "";
    public decimal CrashFrequencyScore { get; set; }    // Weight: 0.35
    public decimal SeverityIndexScore { get; set; }     // Weight: 0.25
    public decimal TrafficVolumeScore { get; set; }     // Weight: 0.10
    public decimal DrainageRiskScore { get; set; }      // Weight: 0.05
    public decimal EnvironmentalScore { get; set; }     // Weight: 0.05
    public decimal CompositeRiskScore { get; set; }     // Note: Weights adjusted without PCI (total = 0.80)
    public RiskLevel RiskLevel { get; set; }
}

public class PersonInfo
{
    public string PersonId { get; set; } = "";
    public int PersonNumber { get; set; }
    public string PersonType { get; set; } = "";
    public KabcoSeverity InjurySeverity { get; set; }
    public int? Age { get; set; }
    public string Gender { get; set; } = "";
    public string EthnicStatus { get; set; } = "";
    public bool AlcoholSuspected { get; set; }
    public bool DrugSuspected { get; set; }
    public List<string> ContributingFactors { get; set; } = new();
}

public class VehicleInfo
{
    public string UnitId { get; set; } = "";
    public int UnitNumber { get; set; }
    public string VehicleType { get; set; } = "";
    public string VehicleModel { get; set; } = "";
    public int? VehicleYear { get; set; }
    public string TravelDirection { get; set; } = "";
    public string MovementPriorToCrash { get; set; } = "";
    public List<string> ContributingFactors { get; set; } = new();
    public List<PersonInfo> Occupants { get; set; } = new();
}

public class ContributingFactor
{
    public string FactorId { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public int Priority { get; set; }
}

public class WeatherCondition
{
    public string Condition { get; set; } = "";
    public string Visibility { get; set; } = "";
    public string SurfaceCondition { get; set; } = "";
    public string LightCondition { get; set; } = "";
}

public class RoadwayCondition
{
    public string RoadType { get; set; } = "";
    public string SurfaceType { get; set; } = "";
    public string SurfaceCondition { get; set; } = "";
    public int? NumberOfLanes { get; set; }
    public string MedianType { get; set; } = "";
    public string WorkZoneType { get; set; } = "";
}

public class RiskSegment
{
    public string SegmentId { get; set; } = "";
    public decimal StartLatitude { get; set; }
    public decimal StartLongitude { get; set; }
    public decimal EndLatitude { get; set; }
    public decimal EndLongitude { get; set; }
    public decimal RiskScore { get; set; }
    public RiskLevel RiskLevel { get; set; }
    public int CrashCount { get; set; }
    public decimal SegmentLength { get; set; }
    public int? Aadt { get; set; }
    public List<CrashRecord> RecentCrashes { get; set; } = new();

    // Road geometry enhancement properties
    public List<double[]> RoadGeometry { get; set; } = new(); // Full road coordinates
    public string RoadLinearId { get; set; } = ""; // TIGER LINEARID
    public string RoadName { get; set; } = ""; // Road name for display
    public string RoadType { get; set; } = ""; // TIGER road type (RTTYP)
    public string GeometryType { get; set; } = "straight_line"; // "actual_road" or "straight_line"

    // Enhanced data for popup display
    public EnvironmentalRiskFactors EnvironmentalFactors { get; set; } = new();
    public decimal SlopePercentage { get; set; }

    // Calculated metrics for popup display
    public decimal CrashesPerMilePerYear { get; set; }
    public int FatalCrashCount { get; set; }
    public int SeriousInjuryCrashCount { get; set; }
    public bool MeetsCrashFrequencyThreshold { get; set; }  // > 5 crashes/mile-year
    public bool MeetsSeverityThreshold { get; set; }        // ≥ 1 fatality or ≥ 3 incapacitating
    public bool MeetsTrafficVolumeThreshold { get; set; }   // > 15,000 AADT
    public bool HasDrainageRisk { get; set; }               // Slope > 5% or hydroplaning incidents
    public bool HasEnvironmentalRisk { get; set; }          // Frequent wet/icy crashes

    // Component scores for potential future real-time recalculation
    public decimal CrashFrequencyScore { get; set; }  // 0-1 normalized
    public decimal SeverityIndexScore { get; set; }   // 0-1 normalized
    public decimal TrafficVolumeScore { get; set; }   // 0-1 normalized
    public decimal DrainageRiskScore { get; set; }    // 0-1 normalized
    public decimal EnvironmentalScore { get; set; }   // 0-1 normalized
}

// Enhanced environmental risk tracking
public class EnvironmentalRiskFactors
{
    public decimal SlopePercentage { get; set; }
    public int WetSurfaceCrashes { get; set; }
    public int IcySurfaceCrashes { get; set; }
    public int FogRelatedCrashes { get; set; }
    public int HydroplaningIncidents { get; set; }
    public bool HasDrainageIssues { get; set; }
}

// Detailed scoring model for enhanced risk calculation
public class DetailedCrisModelScore : CrisModelScore
{
    public decimal CrashFrequencyPerMile { get; set; }
    public int FatalCrashes { get; set; }
    public int IncapacitatingInjuryCrashes { get; set; }
    public decimal SlopePercentage { get; set; }
    public int WeatherRelatedCrashes { get; set; }
    public Dictionary<string, int> CrashBySurfaceCondition { get; set; } = new();
    public Dictionary<string, int> CrashByWeatherCondition { get; set; } = new();
}

public class IntersectionRisk
{
    public string IntersectionId { get; set; } = "";
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public decimal RiskScore { get; set; }
    public RiskLevel RiskLevel { get; set; }
    public int CrashCount { get; set; }
    public List<string> IntersectingRoads { get; set; } = new();
    // Capped (most recent 10) for display; the counts below cover ALL crashes at the intersection.
    public List<CrashRecord> RecentCrashes { get; set; } = new();
    public int FatalCrashCount { get; set; }
    public int InjuryCrashCount { get; set; }
    public int PropertyDamageCrashCount { get; set; }
}

// GeoJSON output models for CRIS data
public class CrisGeoJsonFeature
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "Feature";

    [JsonPropertyName("properties")]
    public Dictionary<string, object> Properties { get; set; } = new();

    [JsonPropertyName("geometry")]
    public CrisGeoJsonGeometry Geometry { get; set; } = null!;
}

public class CrisGeoJsonCollection
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "FeatureCollection";

    [JsonPropertyName("features")]
    public List<CrisGeoJsonFeature> Features { get; set; } = new();

    [JsonPropertyName("metadata")]
    public CrisMetadata? Metadata { get; set; }
}

public class CrisGeoJsonGeometry
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("coordinates")]
    public object Coordinates { get; set; } = null!;
}

public class CrisMetadata
{
    public DateTime GeneratedAt { get; set; }
    public string DataSource { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int TotalCrashes { get; set; }
    public int TrafficEnabledSegments { get; set; }
    public CrisModelWeights ModelWeights { get; set; } = new();
}

public class CrisModelWeights
{
    public decimal CrashFrequency { get; set; } = 0.35m;
    public decimal SeverityIndex { get; set; } = 0.25m;
    public decimal TrafficVolume { get; set; } = 0.15m;       // Increased from 0.10m
    public decimal DrainageRisk { get; set; } = 0.15m;        // Increased from 0.05m
    public decimal Environmental { get; set; } = 0.10m;       // Increased from 0.05m
}

// Configuration models for CRIS data processing
public class CrisModelConfiguration
{
    public CrisModelWeights ModelWeights { get; set; } = new();
    public CrisThresholds Thresholds { get; set; } = new();
    public CrisDataSources DataSources { get; set; } = new();
    public CrisProcessingOptions ProcessingOptions { get; set; } = new();
    public DEMConfiguration DEMConfiguration { get; set; } = new();
}

public class CrisThresholds
{
    public decimal CrashFrequencyPerMile { get; set; } = 5.0m;
    public int FatalCrashThreshold { get; set; } = 1;
    public int IncapacitatingInjuryThreshold { get; set; } = 3;
    public int TrafficVolumeThreshold { get; set; } = 15000;
    public decimal SlopeThreshold { get; set; } = 5.0m;
}

public class CrisDataSources
{
    public string InputDirectory { get; set; } = "";
    public string OutputDirectory { get; set; } = "";
    public string RoadNetworkPath { get; set; } = "";
    public string TrafficRoadsPath { get; set; } = "";
}

public class CrisProcessingOptions
{
    public decimal SpatialToleranceMeters { get; set; } = 50.0m;
    public decimal MinimumSegmentLengthMiles { get; set; } = 0.1m;
    public bool EnableElevationAnalysis { get; set; } = true;
    public bool EnableEnvironmentalAnalysis { get; set; } = true;
}

public class DEMConfiguration
{
    public string SlopeRasterPath { get; set; } = "";
    public bool EnableDEMSampling { get; set; } = true;
}

public class CrisConfiguration
{
    public string DataPath { get; set; } = "/cris-data/";
    public CrisModelWeights ModelWeights { get; set; } = new();
    public List<CrisLayerConfig> Layers { get; set; } = new();
    public CrisBounds ParkerCountyBounds { get; set; } = new();
}

public class CrisLayerConfig : LayerConfig
{
    public CrisLayerType LayerType { get; set; }
    public CrisLayerProperties? LayerProperties { get; set; }
    public List<string> FilterOptions { get; set; } = new();
}

// Base class for layer properties
public class CrisLayerProperties
{
}

// Crash points layer properties
public class CrashPointsLayerProperties : CrisLayerProperties
{
    public int RadiusScale { get; set; } = 50;
    public int RadiusMinPixels { get; set; } = 3;
    public int RadiusMaxPixels { get; set; } = 30;
    public string GetPositionFunction { get; set; } = "getCrashPosition";
    public string GetRadiusFunction { get; set; } = "getCrashRadius";
    public string GetFillColorFunction { get; set; } = "getCrashColor";
}

// Risk segments layer properties
public class RiskSegmentsLayerProperties : CrisLayerProperties
{
    public int WidthScale { get; set; } = 20;
    public int WidthMinPixels { get; set; } = 2;
    public int WidthMaxPixels { get; set; } = 12;
    public string GetPathFunction { get; set; } = "getRiskSegmentPath";
    public string GetWidthFunction { get; set; } = "getRiskSegmentWidth";
    public string GetColorFunction { get; set; } = "getRiskSegmentColor";
}

// Risk heatmap layer properties
public class RiskHeatmapLayerProperties : CrisLayerProperties
{
    public int RadiusPixels { get; set; } = 50;
    public double Intensity { get; set; } = 1.0;
    public double Threshold { get; set; } = 0.03;
    public string GetPositionFunction { get; set; } = "getCrashPosition";
    public string GetWeightFunction { get; set; } = "getCrashHeatmapWeight";
    public int[][] ColorRange { get; set; } = new[]
    {
        new[] { 255, 255, 204, 0 },    // Transparent yellow
        new[] { 255, 237, 160, 63 },   // Light yellow
        new[] { 254, 217, 118, 127 },  // Yellow
        new[] { 254, 178, 76, 191 },   // Orange
        new[] { 253, 141, 60, 255 },   // Dark orange
        new[] { 240, 59, 32, 255 },    // Red
        new[] { 189, 0, 38, 255 }      // Dark red
    };
}

// Intersection risks layer properties
public class IntersectionRisksLayerProperties : CrisLayerProperties
{
    public int RadiusScale { get; set; } = 100;
    public int RadiusMinPixels { get; set; } = 5;
    public int RadiusMaxPixels { get; set; } = 50;
    public string GetPositionFunction { get; set; } = "getIntersectionPosition";
    public string GetRadiusFunction { get; set; } = "getIntersectionRadius";
    public string GetFillColorFunction { get; set; } = "getIntersectionColor";
    public bool Stroked { get; set; } = true;
    public int LineWidthMinPixels { get; set; } = 2;
    public int[] LineColor { get; set; } = new[] { 255, 255, 255, 200 };
}

public class CrisBounds
{
    public decimal MinLatitude { get; set; } = 32.5m;
    public decimal MaxLatitude { get; set; } = 33.0m;
    public decimal MinLongitude { get; set; } = -98.0m;
    public decimal MaxLongitude { get; set; } = -97.0m;
}

// CSV parsing models for CRIS data import
public class CrashCsvRecord
{
    [Name("Crash_ID")]
    public string? CrashId { get; set; }

    [Name("Crash_Date")]
    public string? CrashDate { get; set; }

    [Name("Crash_Time")]
    public string? CrashTime { get; set; }

    [Name("Day_of_Week")]
    public string? DayOfWeek { get; set; }

    [Name("Cnty_ID")]
    public string? County { get; set; }

    [Name("City_ID")]
    public string? City { get; set; }

    [Name("Latitude")]
    public string? Latitude { get; set; }

    [Name("Longitude")]
    public string? Longitude { get; set; }

    [Name("Crash_Sev_ID")]
    public string? CrashSeverity { get; set; }

    [Name("Death_Cnt")]
    public string? PersonsKilled { get; set; }

    [Name("Private_Dr_Fl")]
    public string? PrivatePropertyFlag { get; set; }

    [Name("Located_Fl")]
    public string? LocatedFlag { get; set; }

    [Name("Sus_Serious_Injry_Cnt")]
    public string? SeriousInjuries { get; set; }

    [Name("Nonincap_Injry_Cnt")]
    public string? NonIncapInjuries { get; set; }

    [Name("Poss_Injry_Cnt")]
    public string? PossibleInjuries { get; set; }

    [Name("Non_Injry_Cnt")]
    public string? NoInjuries { get; set; }

    [Name("Tot_Injry_Cnt")]
    public string? TotalInjuries { get; set; }

    [Name("Wthr_Cond_ID")]
    public string? WeatherCondition { get; set; }

    [Name("Light_Cond_ID")]
    public string? LightCondition { get; set; }

    [Name("Surf_Cond_ID")]
    public string? SurfaceCondition { get; set; }

    [Name("Traffic_Cntl_ID")]
    public string? TrafficControl { get; set; }
}

public class PersonCsvRecord
{
    [Name("Crash_ID")]
    public string? CrashId { get; set; }

    [Name("Unit_Nbr")]
    public string? UnitNumber { get; set; }

    [Name("Prsn_Nbr")]
    public string? PersonNumber { get; set; }

    [Name("Prsn_Type_ID")]
    public string? PersonType { get; set; }

    [Name("Prsn_Injry_Sev_ID")]
    public string? InjurySeverity { get; set; }

    [Name("Prsn_Age")]
    public string? Age { get; set; }

    [Name("Prsn_Ethnicity_ID")]
    public string? EthnicStatus { get; set; }

    [Name("Prsn_Gndr_ID")]
    public string? Gender { get; set; }

    [Name("Prsn_Alc_Rslt_ID")]
    public string? AlcoholSuspected { get; set; }

    [Name("Prsn_Drg_Rslt_ID")]
    public string? DrugSuspected { get; set; }
}

public class UnitCsvRecord
{
    [Name("Crash_ID")]
    public string? CrashId { get; set; }

    [Name("Unit_Nbr")]
    public string? UnitNumber { get; set; }

    [Name("Unit_Desc_ID")]
    public string? VehicleType { get; set; }

    [Name("Veh_Mod_ID")]
    public string? VehicleModel { get; set; }

    [Name("Veh_Mod_Year")]
    public string? VehicleYear { get; set; }

    [Name("Veh_Trvl_Dir_ID")]
    public string? TravelDirection { get; set; }

    [Name("First_Harm_Evt_Inv_ID")]
    public string? MovementPriorToCrash { get; set; }
}

// Enums
public enum KabcoSeverity
{
    Unknown = 0,
    K_Fatal = 1,           // K - Fatal injury
    A_IncapacitatingInjury = 2,  // A - Incapacitating injury
    B_NonIncapacitatingInjury = 3, // B - Non-incapacitating injury
    C_PossibleInjury = 4,  // C - Possible injury
    O_NoInjury = 5         // O - No injury (Property damage only)
}

public enum RiskLevel
{
    VeryLow = 1,
    Low = 2,
    Moderate = 3,
    High = 4,
    VeryHigh = 5
}

public enum CrisLayerType
{
    CrashPoints,
    RiskSegments,
    RiskHeatmap,
    IntersectionRisks,
    ModelDashboard
}

// Spatial analysis models
public class BoundingBox
{
    public decimal MinLatitude { get; set; }
    public decimal MaxLatitude { get; set; }
    public decimal MinLongitude { get; set; }
    public decimal MaxLongitude { get; set; }
}

public class SpatialJoinResult
{
    public CrashRecord Crash { get; set; } = null!;
    public string RoadSegmentId { get; set; } = "";
    public decimal DistanceToRoad { get; set; }
    public bool WithinThreshold { get; set; }
}

// Statistics and aggregation models
public class CrashStatistics
{
    public int TotalCrashes { get; set; }
    public int FatalCrashes { get; set; }
    public int InjuryCrashes { get; set; }
    public int PropertyDamageOnlyCrashes { get; set; }
    public decimal AverageCrashesPerMile { get; set; }
    public decimal AverageCrashesPerYear { get; set; }
    public Dictionary<KabcoSeverity, int> SeverityBreakdown { get; set; } = new();
    public Dictionary<string, int> ContributingFactorCounts { get; set; } = new();
}

public class TemporalAggregation
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int DayOfWeek { get; set; }
    public int Hour { get; set; }
    public int CrashCount { get; set; }
    public List<KabcoSeverity> Severities { get; set; } = new();
}

// Event args for CRIS filter changes
public class CrisFilterEventArgs
{
    public string LayerId { get; set; }
    public string FilterType { get; set; }
    public string? FilterValue { get; set; }

    public CrisFilterEventArgs(string layerId, string filterType, string? filterValue)
    {
        LayerId = layerId;
        FilterType = filterType;
        FilterValue = filterValue;
    }
}

// Deck.gl format models - strongly typed objects that match what deck.gl expects
public class CrashPointDeckGl
{
    public string CrashId { get; set; } = "";
    public string CrashDate { get; set; } = "";
    public string CrashTime { get; set; } = "";
    public string CrashDateTime { get; set; } = "";
    public string Severity { get; set; } = "";
    public string SeverityCode { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int PersonsInvolved { get; set; }
    public int VehiclesInvolved { get; set; }
    public string WeatherCondition { get; set; } = "";
    public string LightCondition { get; set; } = "";
    public string SurfaceCondition { get; set; } = "";
    public string RoadwayId { get; set; } = "";
    public int? Aadt { get; set; }
    public int FatalCount { get; set; }
    public int InjuryCount { get; set; }
    public string[] ContributingFactors { get; set; } = Array.Empty<string>();
    public double[] Coordinates { get; set; } = new double[2];

    // DEM-derived slope information
    public double SlopeAtLocation { get; set; }  // Degrees from DEM
    public double SlopePercentage { get; set; }  // Percentage grade
    public string SlopeCategory { get; set; } = ""; // Flat/Moderate/Steep
}

public class RiskSegmentDeckGl
{
    public string SegmentId { get; set; } = "";
    public double RiskScore { get; set; }
    public string RiskLevel { get; set; } = "";
    public int RiskLevelNumeric { get; set; }
    public int CrashCount { get; set; }
    public int? Aadt { get; set; }
    public double SegmentLength { get; set; }
    public double CrashesPerMile { get; set; }
    public CrashSummaryDeckGl[] RecentCrashes { get; set; } = Array.Empty<CrashSummaryDeckGl>();
    public double StartLatitude { get; set; }
    public double StartLongitude { get; set; }
    public double EndLatitude { get; set; }
    public double EndLongitude { get; set; }
    public double[][] Coordinates { get; set; } = Array.Empty<double[]>();

    // Road geometry enhancement properties
    public string RoadLinearId { get; set; } = "";
    public string RoadName { get; set; } = "";
    public string RoadType { get; set; } = "";
    public string GeometryType { get; set; } = "straight_line";

    // Fatal and injury crash counts for severity analysis
    public int FatalCrashes { get; set; }
    public int InjuryCrashes { get; set; }
    public int PropertyDamageCrashes { get; set; }

    // Environmental factors
    public int WetSurfaceCrashes { get; set; }
    public int IcySurfaceCrashes { get; set; }
    public int FogRelatedCrashes { get; set; }
    public int HydroplaningIncidents { get; set; }
    public double SlopePercentage { get; set; }
    public bool HasDrainageIssues { get; set; }
    public bool HasEnvironmentalRisk { get; set; }
}

public class IntersectionRiskDeckGl
{
    public string IntersectionId { get; set; } = "";
    public double RiskScore { get; set; }
    public string RiskLevel { get; set; } = "";
    public int RiskLevelNumeric { get; set; }
    public int CrashCount { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string[] IntersectingRoads { get; set; } = Array.Empty<string>();
    public CrashSummaryDeckGl[] RecentCrashes { get; set; } = Array.Empty<CrashSummaryDeckGl>();
    public int FatalCrashes { get; set; }
    public int InjuryCrashes { get; set; }
    public int PropertyDamageCrashes { get; set; }
    public double[] Coordinates { get; set; } = new double[2];
}

public class CrashSummaryDeckGl
{
    public string CrashId { get; set; } = "";
    public string CrashDate { get; set; } = "";
    public string Severity { get; set; } = "";
    public string SeverityCode { get; set; } = "";
    public int PersonsInvolved { get; set; }
    public int VehiclesInvolved { get; set; }
    public int FatalCount { get; set; }
    public int InjuryCount { get; set; }

    // DEM-derived slope information
    public decimal SlopeAtLocation { get; set; }  // Degrees from DEM
    public decimal SlopePercentage { get; set; }  // Percentage grade
    public string SlopeCategory { get; set; } = ""; // Flat/Moderate/Steep
}

public class IntersectionPopupData
{
    public string IntersectionId { get; set; } = "";
    public double RiskScore { get; set; }
    public RiskLevel RiskLevel { get; set; }
    public int CrashCount { get; set; }
    public int FatalCrashes { get; set; }
    public int InjuryCrashes { get; set; }
    public int PropertyDamageCrashes { get; set; }
    public List<string> ResolvedRoads { get; set; } = new();
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public List<CrashRecord> RecentCrashes { get; set; } = new();
}

public class DeckGlDataCollection<T>
{
    public T[] Data { get; set; } = Array.Empty<T>();
}