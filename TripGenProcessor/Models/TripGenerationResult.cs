namespace TripGenProcessor.Models;

/// <summary>
/// Trip generation result for a single parcel.
/// This is the core output written to the parcels-with-trips GeoJSON.
/// </summary>
public class TripGenerationResult
{
    public string ParcelId { get; set; } = "";
    public string StateCd { get; set; } = "";
    public int? IteCode { get; set; }
    public string IteLandUse { get; set; } = "";
    public string IteUnit { get; set; } = "";
    public double Units { get; set; }                    // Dwelling units, 1000 sqft, students, etc.
    public double DailyRate { get; set; }
    public double DailyTrips { get; set; }
    public double AmPeakTrips { get; set; }
    public double PmPeakTrips { get; set; }
    public double DirectionalSplit { get; set; } = 0.50;
    public string? AccessSegmentId { get; set; }         // Nearest TIGER road linearId
    public string? AccessRoadName { get; set; }
    public double? SnapDistanceMeters { get; set; }
    public string Classification { get; set; } = "";     // e.g. "Residential", "Commercial"
    public string? Notes { get; set; }                   // Classification notes / warnings
}

/// <summary>
/// Summary statistics for the entire trip generation run.
/// </summary>
public class TripGenerationSummary
{
    public string CityName { get; set; } = "";
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    public int TotalParcels { get; set; }
    public int ParcelsWithTrips { get; set; }
    public int ParcelsSkipped { get; set; }
    public double TotalDailyTrips { get; set; }
    public double TotalAmPeakTrips { get; set; }
    public double TotalPmPeakTrips { get; set; }
    public int RoadSegmentsLinked { get; set; }
    public Dictionary<string, CategorySummary> ByCategory { get; set; } = new();
    public Dictionary<string, RoadSummary> TopRoads { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class CategorySummary
{
    public int ParcelCount { get; set; }
    public double TotalUnits { get; set; }
    public double DailyTrips { get; set; }
    public double AmPeakTrips { get; set; }
    public double PmPeakTrips { get; set; }
}

public class RoadSummary
{
    public string RoadName { get; set; } = "";
    public double DailyTrips { get; set; }
    public int? Aadt { get; set; }
    public int ParcelCount { get; set; }
    public double? VolumeToCapacityRatio { get; set; }
}
