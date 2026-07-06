using NetTopologySuite.Geometries;

namespace TripGenProcessor.Models;

/// <summary>
/// Represents a parcel from the Parker County CAD dataset.
/// Properties mapped from GeoJSON feature attributes.
/// </summary>
public class CadParcel
{
    public string ParcelId { get; set; } = "";
    public string StateCd { get; set; } = "";          // Texas property type code (A1, F1, etc.)
    public string? SitusStreet { get; set; }
    public string? SitusCity { get; set; }
    public string? OwnerName { get; set; }
    public double LegalAcreage { get; set; }
    public double ImprvSqft { get; set; }               // Improvement square footage
    public double ImprvVal { get; set; }                 // Improvement value
    public double LandVal { get; set; }
    public double TotalVal { get; set; }
    public int? YearBuilt { get; set; }
    public int? DwellingUnits { get; set; }              // For multi-family parcels
    public Geometry? Geometry { get; set; }
    public Point? Centroid => Geometry?.Centroid;
}

/// <summary>
/// Represents a TIGER road segment with optional AADT data.
/// </summary>
public class RoadSegment
{
    public string LinearId { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Mtfcc { get; set; }                   // MAF/TIGER Feature Class Code
    public int? Aadt { get; set; }                       // Annual Average Daily Traffic
    public int? AadtYear { get; set; }
    public string? FunctionalClass { get; set; }
    public Geometry? Geometry { get; set; }

    // Aggregated trip data (populated by TripAggregator)
    public double TotalDailyTrips { get; set; }
    public double TotalAmPeakTrips { get; set; }
    public double TotalPmPeakTrips { get; set; }
    public int ParcelCount { get; set; }
    public double? VolumeToCapacityRatio { get; set; }
}
