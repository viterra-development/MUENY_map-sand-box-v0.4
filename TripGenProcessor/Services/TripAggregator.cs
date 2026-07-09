using Microsoft.Extensions.Logging;
using TripGenProcessor.Models;

namespace TripGenProcessor.Services;

/// <summary>
/// Aggregates parcel-level trip data onto road segments.
/// Produces per-road volume totals and a comprehensive summary report.
/// </summary>
public class TripAggregator
{
    private readonly ILogger<TripAggregator> _logger;

    public TripAggregator(ILogger<TripAggregator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Sum trips from all parcels onto their linked road segments.
    /// </summary>
    public void AggregateToRoads(List<TripGenerationResult> results, List<RoadSegment> roads)
    {
        _logger.LogInformation("Aggregating trips to road segments...");

        // Build road lookup. TIGER LinearId is usually unique but some segments
        // ship with empty or duplicate ids after preprocessing. Skip duplicates.
        var roadMap = new Dictionary<string, RoadSegment>(roads.Count);
        foreach (var r in roads)
        {
            if (!string.IsNullOrEmpty(r.LinearId) && !roadMap.ContainsKey(r.LinearId))
                roadMap[r.LinearId] = r;
        }

        // Reset road totals
        foreach (var road in roads)
        {
            road.TotalDailyTrips = 0;
            road.TotalAmPeakTrips = 0;
            road.TotalPmPeakTrips = 0;
            road.ParcelCount = 0;
        }

        foreach (var result in results)
        {
            if (result.AccessSegmentId == null || result.DailyTrips <= 0)
                continue;

            if (roadMap.TryGetValue(result.AccessSegmentId, out var road))
            {
                road.TotalDailyTrips += result.DailyTrips;
                road.TotalAmPeakTrips += result.AmPeakTrips;
                road.TotalPmPeakTrips += result.PmPeakTrips;
                road.ParcelCount++;
            }
        }

        // Calculate V/C ratios for roads with AADT data
        int roadsWithTrips = 0;
        foreach (var road in roads)
        {
            if (road.TotalDailyTrips > 0)
            {
                roadsWithTrips++;
                if (road.Aadt.HasValue && road.Aadt.Value > 0)
                {
                    road.VolumeToCapacityRatio = Math.Round(
                        road.TotalDailyTrips / road.Aadt.Value, 3);
                }
            }
        }

        _logger.LogInformation("{RoadsWithTrips} road segments have trip generation data", roadsWithTrips);
    }

    /// <summary>
    /// Generate a comprehensive summary of the trip generation results.
    /// </summary>
    public TripGenerationSummary GenerateSummary(
        string cityName,
        List<CadParcel> parcels,
        List<TripGenerationResult> results,
        List<RoadSegment> roads)
    {
        var summary = new TripGenerationSummary
        {
            CityName = cityName,
            ProcessedAt = DateTime.UtcNow,
            TotalParcels = parcels.Count,
            ParcelsWithTrips = results.Count(r => r.DailyTrips > 0),
            ParcelsSkipped = results.Count(r => r.DailyTrips <= 0),
            TotalDailyTrips = Math.Round(results.Sum(r => r.DailyTrips), 0),
            TotalAmPeakTrips = Math.Round(results.Sum(r => r.AmPeakTrips), 0),
            TotalPmPeakTrips = Math.Round(results.Sum(r => r.PmPeakTrips), 0),
            RoadSegmentsLinked = results.Count(r => r.AccessSegmentId != null && r.DailyTrips > 0)
        };

        // By category
        var byCategory = results
            .Where(r => r.DailyTrips > 0)
            .GroupBy(r => r.Classification)
            .ToDictionary(
                g => g.Key,
                g => new CategorySummary
                {
                    ParcelCount = g.Count(),
                    TotalUnits = Math.Round(g.Sum(r => r.Units), 1),
                    DailyTrips = Math.Round(g.Sum(r => r.DailyTrips), 0),
                    AmPeakTrips = Math.Round(g.Sum(r => r.AmPeakTrips), 0),
                    PmPeakTrips = Math.Round(g.Sum(r => r.PmPeakTrips), 0),
                });
        summary.ByCategory = byCategory;

        // Top roads by trip volume. LinearIds can duplicate across TIGER exports —
        // fall back to positional key when a collision occurs.
        var topRoadCandidates = roads
            .Where(r => r.TotalDailyTrips > 0)
            .OrderByDescending(r => r.TotalDailyTrips)
            .Take(20)
            .ToList();
        var topRoads = new Dictionary<string, RoadSummary>(topRoadCandidates.Count);
        int idx = 0;
        foreach (var r in topRoadCandidates)
        {
            var key = string.IsNullOrEmpty(r.LinearId) ? $"seg-{idx}" : r.LinearId;
            while (topRoads.ContainsKey(key)) { idx++; key = $"seg-{idx}"; }
            topRoads[key] = new RoadSummary
            {
                RoadName = r.FullName,
                DailyTrips = Math.Round(r.TotalDailyTrips, 0),
                Aadt = r.Aadt,
                ParcelCount = r.ParcelCount,
                VolumeToCapacityRatio = r.VolumeToCapacityRatio
            };
            idx++;
        }
        summary.TopRoads = topRoads;

        // Warnings
        var highVcRoads = roads
            .Where(r => r.VolumeToCapacityRatio > 0.5)
            .OrderByDescending(r => r.VolumeToCapacityRatio);

        foreach (var road in highVcRoads)
        {
            summary.Warnings.Add(
                $"⚠️ {road.FullName}: V/C ratio {road.VolumeToCapacityRatio:F2} " +
                $"({road.TotalDailyTrips:N0} generated trips vs {road.Aadt:N0} AADT)");
        }

        var unlinkedParcels = results.Count(r => r.DailyTrips > 0 && r.AccessSegmentId == null);
        if (unlinkedParcels > 0)
        {
            summary.Warnings.Add($"⚠️ {unlinkedParcels} parcels with trips not linked to any road segment");
        }

        return summary;
    }
}
