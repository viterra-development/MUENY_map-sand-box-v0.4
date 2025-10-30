using Microsoft.Extensions.Logging;
using TCDS.Importer.Models;

namespace TCDS.Importer.Services;

/// <summary>
/// Implements spatial interpolation using Inverse Distance Weighting (IDW) for traffic estimation
/// Phase 1: Distance-weighted interpolation (not true geostatistical kriging)
/// </summary>
public class SpatialInterpolationEstimator
{
    private readonly ILogger<SpatialInterpolationEstimator> _logger;
    private readonly InterpolationConfig _config;

    // Compatible road hierarchies for fallback matching
    private static readonly Dictionary<RoadHierarchy, List<RoadHierarchy>> CompatibleHierarchies = new()
    {
        { RoadHierarchy.Interstate, new() { RoadHierarchy.USHighway } },
        { RoadHierarchy.USHighway, new() { RoadHierarchy.Interstate, RoadHierarchy.StateHighway } },
        { RoadHierarchy.StateHighway, new() { RoadHierarchy.USHighway, RoadHierarchy.Arterial } },
        { RoadHierarchy.Arterial, new() { RoadHierarchy.StateHighway, RoadHierarchy.LocalRoad } },
        { RoadHierarchy.LocalRoad, new() { RoadHierarchy.Arterial } },
        { RoadHierarchy.Ramp, new() { RoadHierarchy.Interstate, RoadHierarchy.USHighway } }
    };

    public SpatialInterpolationEstimator(
        ILogger<SpatialInterpolationEstimator> logger,
        InterpolationConfig? config = null)
    {
        _logger = logger;
        _config = config ?? new InterpolationConfig();
    }

    /// <summary>
    /// Estimates AADT for a target road using spatial interpolation (IDW)
    /// </summary>
    public AadtEstimation? EstimateAadt(
        RoadSegment targetRoad,
        List<RoadSegment> referenceRoads)
    {
        // Step 1: Find nearby reference roads with same hierarchy
        var neighbors = FindNearestNeighbors(targetRoad, referenceRoads, _config.RequireSameHierarchy);

        // Step 2: If no same-hierarchy neighbors, try compatible hierarchies
        if (!neighbors.Any() && _config.RequireSameHierarchy)
        {
            _logger.LogDebug("No same-hierarchy neighbors found for {RoadName}, trying compatible hierarchies",
                targetRoad.FullName);
            neighbors = FindNearestNeighbors(targetRoad, referenceRoads, requireSameHierarchy: false);
        }

        // Step 3: If still no neighbors, cannot estimate
        if (!neighbors.Any())
        {
            _logger.LogDebug("No suitable neighbors found for {RoadName} within {MaxRadius}m",
                targetRoad.FullName, _config.MaxSearchRadius);
            return null;
        }

        // Step 4: Calculate weights using exponential decay
        var weightedNeighbors = CalculateExponentialWeights(neighbors);

        // Step 5: Calculate weighted AADT estimate
        var estimatedAadt = CalculateWeightedAverage(weightedNeighbors);

        // Step 6: Calculate confidence score
        var confidence = CalculateConfidence(weightedNeighbors, targetRoad.Hierarchy);

        // Step 7: Check if confidence meets threshold
        if (confidence < _config.MinConfidence)
        {
            _logger.LogDebug("Confidence {Confidence:F2} below threshold {Threshold:F2} for {RoadName}",
                confidence, _config.MinConfidence, targetRoad.FullName);
            return null;
        }

        // Step 8: Build estimation result
        var estimation = new AadtEstimation
        {
            EstimatedAadt = estimatedAadt,
            Confidence = confidence,
            Method = "SpatialInterpolation_IDW",
            SourceRoads = weightedNeighbors.Select(wn => new SourceRoadInfo
            {
                LinearId = wn.Neighbor.LinearId,
                Aadt = wn.Neighbor.ExistingAadt!.Value,
                DistanceMeters = wn.Distance,
                Weight = wn.Weight,
                Hierarchy = wn.Neighbor.Hierarchy
            }).ToList()
        };

        // Step 9: Add warnings for cross-hierarchy estimation
        if (weightedNeighbors.Any(wn => wn.Neighbor.Hierarchy != targetRoad.Hierarchy))
        {
            var hierarchies = string.Join(", ", weightedNeighbors
                .Select(wn => wn.Neighbor.Hierarchy.ToString())
                .Distinct());
            estimation.Warnings.Add($"Estimated using mixed hierarchies: {hierarchies}");
        }

        // Step 10: Add warning if using distant neighbors
        var maxDistance = weightedNeighbors.Max(wn => wn.Distance);
        if (maxDistance > 2000)
        {
            estimation.Warnings.Add($"Nearest reference road is {maxDistance:F0}m away");
        }

        return estimation;
    }

    /// <summary>
    /// Finds nearest neighbors within search radius
    /// </summary>
    private List<NeighborInfo> FindNearestNeighbors(
        RoadSegment targetRoad,
        List<RoadSegment> referenceRoads,
        bool requireSameHierarchy)
    {
        var neighbors = new List<NeighborInfo>();

        foreach (var refRoad in referenceRoads)
        {
            // Skip if no AADT data
            if (!refRoad.ExistingAadt.HasValue || refRoad.ExistingAadt.Value <= 0)
                continue;

            // Skip self
            if (refRoad.LinearId == targetRoad.LinearId)
                continue;

            // Check hierarchy compatibility
            if (requireSameHierarchy)
            {
                if (refRoad.Hierarchy != targetRoad.Hierarchy)
                    continue;
            }
            else
            {
                // Check if hierarchies are compatible
                if (!IsCompatibleHierarchy(targetRoad.Hierarchy, refRoad.Hierarchy))
                    continue;
            }

            // Calculate distance
            var distance = CalculateDistance(
                targetRoad.CentroidLatitude, targetRoad.CentroidLongitude,
                refRoad.CentroidLatitude, refRoad.CentroidLongitude);

            // Check if within search radius
            if (distance > _config.MaxSearchRadius)
                continue;

            neighbors.Add(new NeighborInfo
            {
                Neighbor = refRoad,
                Distance = distance
            });
        }

        // Sort by distance and take top N
        return neighbors
            .OrderBy(n => n.Distance)
            .Take(_config.MaxNeighbors)
            .ToList();
    }

    /// <summary>
    /// Checks if two road hierarchies are compatible for estimation
    /// </summary>
    private bool IsCompatibleHierarchy(RoadHierarchy target, RoadHierarchy reference)
    {
        // Same hierarchy is always compatible
        if (target == reference)
            return true;

        // Check compatibility table
        if (CompatibleHierarchies.TryGetValue(target, out var compatibleList))
        {
            return compatibleList.Contains(reference);
        }

        return false;
    }

    /// <summary>
    /// Calculates exponential decay weights for neighbors
    /// w_i = exp(-distance_i / λ)
    /// </summary>
    private List<WeightedNeighbor> CalculateExponentialWeights(List<NeighborInfo> neighbors)
    {
        var weightedNeighbors = new List<WeightedNeighbor>();

        foreach (var neighbor in neighbors)
        {
            // Exponential decay: w = exp(-d/λ)
            var weight = Math.Exp(-neighbor.Distance / _config.DecayLambda);

            weightedNeighbors.Add(new WeightedNeighbor
            {
                Neighbor = neighbor.Neighbor,
                Distance = neighbor.Distance,
                Weight = weight
            });
        }

        return weightedNeighbors;
    }

    /// <summary>
    /// Calculates weighted average AADT from weighted neighbors
    /// AADT_estimated = Σ(w_i × AADT_i) / Σ(w_i)
    /// </summary>
    private int CalculateWeightedAverage(List<WeightedNeighbor> weightedNeighbors)
    {
        var totalWeight = weightedNeighbors.Sum(wn => wn.Weight);
        var weightedSum = weightedNeighbors.Sum(wn => wn.Weight * wn.Neighbor.ExistingAadt!.Value);

        return (int)Math.Round(weightedSum / totalWeight);
    }

    /// <summary>
    /// Calculates confidence score based on neighbor characteristics
    /// </summary>
    private double CalculateConfidence(List<WeightedNeighbor> weightedNeighbors, RoadHierarchy targetHierarchy)
    {
        // Base confidence from number of neighbors (0.5 - 1.0)
        var neighborFactor = Math.Min(weightedNeighbors.Count / (double)_config.MaxNeighbors, 1.0) * 0.3 + 0.5;

        // Distance factor (closer = higher confidence)
        var avgDistance = weightedNeighbors.Average(wn => wn.Distance);
        var distanceFactor = Math.Exp(-avgDistance / (_config.DecayLambda * 2));

        // Hierarchy consistency factor
        var sameHierarchyCount = weightedNeighbors.Count(wn => wn.Neighbor.Hierarchy == targetHierarchy);
        var hierarchyFactor = sameHierarchyCount / (double)weightedNeighbors.Count;

        // Combined confidence score
        var confidence = (neighborFactor * 0.3) + (distanceFactor * 0.4) + (hierarchyFactor * 0.3);

        return Math.Max(0.0, Math.Min(1.0, confidence));
    }

    /// <summary>
    /// Calculates distance between two points in meters using Haversine formula
    /// </summary>
    private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000; // Earth radius in meters

        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLon = (lon2 - lon1) * Math.PI / 180.0;

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return R * c;
    }

    // Helper classes
    private class NeighborInfo
    {
        public RoadSegment Neighbor { get; set; } = null!;
        public double Distance { get; set; }
    }

    private class WeightedNeighbor
    {
        public RoadSegment Neighbor { get; set; } = null!;
        public double Distance { get; set; }
        public double Weight { get; set; }
    }
}
