using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Distance;
using TripGenProcessor.Models;

namespace TripGenProcessor.Services;

/// <summary>
/// Links parcels to their nearest road segment using spatial indexing.
/// Uses an R-tree (STRtree) for efficient nearest-neighbor queries.
/// </summary>
public class RoadLinker
{
    private readonly ILogger<RoadLinker> _logger;
    private readonly double _maxSnapDistanceMeters;
    private STRtree<RoadSegment>? _index;
    private List<RoadSegment>? _roads;

    // Approximate meters per degree at Parker County latitude (~32.75°N)
    private const double MetersPerDegreeLat = 111132.0;
    private const double MetersPerDegreeLng = 94000.0; // cos(32.75°) * 111132

    public RoadLinker(ILogger<RoadLinker> logger, double maxSnapDistanceMeters = 500.0)
    {
        _logger = logger;
        _maxSnapDistanceMeters = maxSnapDistanceMeters;
    }

    /// <summary>
    /// Build spatial index from road segments.
    /// </summary>
    public void BuildIndex(List<RoadSegment> roads)
    {
        _roads = roads;
        _index = new STRtree<RoadSegment>();

        foreach (var road in roads)
        {
            if (road.Geometry != null)
                _index.Insert(road.Geometry.EnvelopeInternal, road);
        }

        _index.Build();
        _logger.LogInformation("Road spatial index built with {Count} segments", roads.Count);
    }

    /// <summary>
    /// Find the nearest road segment to a parcel centroid.
    /// Returns null if no road is within the max snap distance.
    /// </summary>
    public (RoadSegment? road, double distanceMeters) FindNearestRoad(CadParcel parcel)
    {
        if (_index == null || _roads == null || parcel.Centroid == null)
            return (null, double.MaxValue);

        var centroid = parcel.Centroid;

        // Create search envelope (buffer in degrees, roughly maxSnapDistance)
        var bufferDeg = _maxSnapDistanceMeters / MetersPerDegreeLat;
        var searchEnv = new Envelope(
            centroid.X - bufferDeg, centroid.X + bufferDeg,
            centroid.Y - bufferDeg, centroid.Y + bufferDeg);

        var candidates = _index.Query(searchEnv);

        RoadSegment? nearest = null;
        double minDistDeg = double.MaxValue;

        foreach (var road in candidates)
        {
            if (road.Geometry == null) continue;

            var dist = centroid.Distance(road.Geometry);
            if (dist < minDistDeg)
            {
                minDistDeg = dist;
                nearest = road;
            }
        }

        if (nearest == null)
            return (null, double.MaxValue);

        // Convert degree distance to approximate meters
        var distMeters = minDistDeg * MetersPerDegreeLat;

        if (distMeters > _maxSnapDistanceMeters)
            return (null, distMeters);

        return (nearest, Math.Round(distMeters, 1));
    }

    /// <summary>
    /// Link all trip generation results to their nearest road segments.
    /// </summary>
    public void LinkAll(List<CadParcel> parcels, List<TripGenerationResult> results)
    {
        if (_index == null)
        {
            _logger.LogError("Road index not built. Call BuildIndex first.");
            return;
        }

        // Build lookup from parcelId → result
        var resultMap = results.ToDictionary(r => r.ParcelId, r => r);

        int linked = 0;
        int unlinked = 0;

        foreach (var parcel in parcels)
        {
            if (!resultMap.TryGetValue(parcel.ParcelId, out var result))
                continue;

            if (result.DailyTrips <= 0)
                continue;

            var (road, distMeters) = FindNearestRoad(parcel);

            if (road != null)
            {
                result.AccessSegmentId = road.LinearId;
                result.AccessRoadName = road.FullName;
                result.SnapDistanceMeters = distMeters;
                linked++;
            }
            else
            {
                unlinked++;
                result.Notes = (result.Notes ?? "") + " | No road within snap distance";
            }
        }

        _logger.LogInformation(
            "Road linking: {Linked} linked, {Unlinked} unlinked (no road within {MaxDist}m)",
            linked, unlinked, _maxSnapDistanceMeters);
    }
}
