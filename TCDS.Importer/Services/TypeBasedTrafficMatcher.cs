using Microsoft.Extensions.Logging;
using TCDS.Importer.Models;
using System.Text.RegularExpressions;

namespace TCDS.Importer.Services;

public class TypeBasedTrafficMatcher
{
    private readonly ILogger<TypeBasedTrafficMatcher> _logger;
    private readonly AadtValidationService _validationService;

    private const double PRECISE_BUFFER = 0.0005; // ~50m for exact matches
    private const double LOOSE_BUFFER = 0.002;    // ~200m for broader search

    public TypeBasedTrafficMatcher(ILogger<TypeBasedTrafficMatcher> logger, AadtValidationService validationService)
    {
        _logger = logger;
        _validationService = validationService;
    }

    public TrafficMatchResult MatchTrafficToRoad(GeoJsonFeature road, List<EnhancedTrafficLocation> trafficLocations)
    {
        var roadHierarchy = DetermineRoadHierarchy(road);
        var roadRoute = ExtractRouteDesignation(road);

        _logger.LogDebug("Matching traffic for road: {RoadName} (hierarchy: {Hierarchy}, route: {Route})",
            GetRoadName(road), roadHierarchy, roadRoute);

        // Step 0: Explicit LocatedOn match (HIGHEST PRIORITY - if location explicitly names this road, use it)
        if (!string.IsNullOrEmpty(roadRoute))
        {
            var locatedOnMatch = FindExplicitLocatedOnMatch(road, trafficLocations, roadRoute, roadHierarchy);
            if (locatedOnMatch != null)
            {
                _logger.LogDebug("Found explicit LocatedOn match for {RoadName}: {LocationId} (LocatedOn: {LocatedOn})",
                    GetRoadName(road), locatedOnMatch.MatchedLocation?.LocationId, locatedOnMatch.MatchedLocation?.LocatedOn);
                return locatedOnMatch;
            }
        }

        // Step 1: Exact route + type match
        var exactMatch = FindExactRouteAndTypeMatch(road, trafficLocations, roadRoute, roadHierarchy);
        if (exactMatch != null)
        {
            _logger.LogDebug("Found exact route and type match for {RoadName}: {LocationId}",
                GetRoadName(road), exactMatch.MatchedLocation?.LocationId);
            return exactMatch;
        }

        // Step 2: Same road type, nearby location (exclude locations with conflicting LocatedOn)
        var typeMatch = FindSameTypeMatch(road, trafficLocations, roadHierarchy, LOOSE_BUFFER, roadRoute);
        if (typeMatch != null)
        {
            _logger.LogDebug("Found same type match for {RoadName}: {LocationId}",
                GetRoadName(road), typeMatch.MatchedLocation?.LocationId);
            return typeMatch;
        }

        // Step 3: Compatible type with warnings (exclude locations with conflicting LocatedOn)
        var compatibleMatch = FindCompatibleTypeMatch(road, trafficLocations, roadHierarchy, roadRoute);
        if (compatibleMatch != null)
        {
            compatibleMatch.AddWarning($"Using {compatibleMatch.SourceType} data for {roadHierarchy} road");
            _logger.LogWarning("Using compatible type match for {RoadName}: {LocationId} ({SourceType} -> {TargetType})",
                GetRoadName(road), compatibleMatch.MatchedLocation?.LocationId,
                compatibleMatch.SourceType, roadHierarchy);
            return compatibleMatch;
        }

        _logger.LogDebug("No traffic match found for road: {RoadName}", GetRoadName(road));
        return TrafficMatchResult.NoMatch();
    }

    private TrafficMatchResult FindExactRouteAndTypeMatch(GeoJsonFeature road,
        List<EnhancedTrafficLocation> locations, string? roadRoute, RoadHierarchy roadHierarchy)
    {
        if (string.IsNullOrEmpty(roadRoute))
            return null;

        var matches = locations
            .Where(l => l.RouteDesignation == roadRoute &&
                       l.TargetRoadHierarchy == roadHierarchy &&
                       l.IsMainlineLocation == IsMainlineRoad(roadHierarchy))
            .Select(l => new { Location = l, Distance = CalculateDistance(road, l) })
            .Where(m => m.Distance <= PRECISE_BUFFER)
            .OrderBy(m => m.Distance)
            .ToList();

        if (!matches.Any())
            return null;

        var bestMatch = matches.First();
        return new TrafficMatchResult
        {
            IsMatch = true,
            MatchedLocation = bestMatch.Location,
            Distance = bestMatch.Distance,
            MatchType = "ExactRouteAndType",
            SourceType = bestMatch.Location.LocationType
        };
    }

    private TrafficMatchResult FindSameTypeMatch(GeoJsonFeature road,
        List<EnhancedTrafficLocation> locations, RoadHierarchy roadHierarchy, double bufferDistance, string? roadRoute = null)
    {
        var matches = locations
            .Where(l => l.TargetRoadHierarchy == roadHierarchy &&
                       l.IsMainlineLocation == IsMainlineRoad(roadHierarchy) &&
                       !HasConflictingLocatedOn(l, roadRoute)) // Exclude locations that explicitly name a different road
            .Select(l => new { Location = l, Distance = CalculateDistance(road, l) })
            .Where(m => m.Distance <= bufferDistance)
            .OrderBy(m => m.Distance)
            .ToList();

        if (!matches.Any())
            return null;

        var bestMatch = matches.First();
        var result = new TrafficMatchResult
        {
            IsMatch = true,
            MatchedLocation = bestMatch.Location,
            Distance = bestMatch.Distance,
            MatchType = "SameType",
            SourceType = bestMatch.Location.LocationType
        };

        // Add warning if distance is significant
        if (bestMatch.Distance > PRECISE_BUFFER)
        {
            result.AddWarning($"Traffic data from {bestMatch.Distance * 111000:F0}m away");
        }

        return result;
    }

    private TrafficMatchResult FindCompatibleTypeMatch(GeoJsonFeature road,
        List<EnhancedTrafficLocation> locations, RoadHierarchy roadHierarchy, string? roadRoute = null)
    {
        // Define compatibility rules
        var compatibleTypes = GetCompatibleHierarchies(roadHierarchy);

        var matches = locations
            .Where(l => compatibleTypes.Contains(l.TargetRoadHierarchy) &&
                       !HasConflictingLocatedOn(l, roadRoute)) // Exclude locations that explicitly name a different road
            .Select(l => new { Location = l, Distance = CalculateDistance(road, l) })
            .Where(m => m.Distance <= LOOSE_BUFFER)
            .OrderBy(m => m.Distance)
            .ToList();

        if (!matches.Any())
            return null;

        var bestMatch = matches.First();
        return new TrafficMatchResult
        {
            IsMatch = true,
            MatchedLocation = bestMatch.Location,
            Distance = bestMatch.Distance,
            MatchType = "Compatible",
            SourceType = bestMatch.Location.LocationType
        };
    }

    private List<RoadHierarchy> GetCompatibleHierarchies(RoadHierarchy targetHierarchy)
    {
        return targetHierarchy switch
        {
            RoadHierarchy.Interstate => new List<RoadHierarchy> { RoadHierarchy.USHighway }, // Only allow highways for interstates as fallback
            RoadHierarchy.USHighway => new List<RoadHierarchy> { RoadHierarchy.StateHighway, RoadHierarchy.Arterial },
            RoadHierarchy.StateHighway => new List<RoadHierarchy> { RoadHierarchy.USHighway, RoadHierarchy.Arterial },
            RoadHierarchy.Arterial => new List<RoadHierarchy> { RoadHierarchy.StateHighway, RoadHierarchy.LocalRoad },
            RoadHierarchy.LocalRoad => new List<RoadHierarchy> { RoadHierarchy.Arterial },
            RoadHierarchy.Ramp => new List<RoadHierarchy>(), // Ramps should never use other types
            _ => new List<RoadHierarchy>()
        };
    }

    public RoadClassification ClassifyRoad(GeoJsonFeature road)
    {
        var properties = road.Properties ?? new Dictionary<string, object?>();
        var roadType = GetPropertyValue(properties, "RTTYP");
        var fullName = GetPropertyValue(properties, "FULLNAME");
        var mtfcc = GetPropertyValue(properties, "MTFCC");

        var hierarchy = DetermineRoadHierarchy(roadType, fullName, mtfcc);
        var routeDesignation = ExtractRouteDesignation(road);

        return new RoadClassification
        {
            Hierarchy = hierarchy,
            RouteDesignation = routeDesignation,
            IsMainlineRoad = hierarchy != RoadHierarchy.Ramp,
            FunctionalClass = GetPropertyValue(properties, "MTFCC")
        };
    }

    public RoadHierarchy DetermineRoadHierarchy(GeoJsonFeature road)
    {
        var properties = road.Properties ?? new Dictionary<string, object?>();
        var roadType = GetPropertyValue(properties, "RTTYP");
        var fullName = GetPropertyValue(properties, "FULLNAME");
        var mtfcc = GetPropertyValue(properties, "MTFCC");

        return DetermineRoadHierarchy(roadType, fullName, mtfcc);
    }

    private RoadHierarchy DetermineRoadHierarchy(string? roadType, string? fullName, string? mtfcc)
    {
        // Interstate highways
        if (roadType == "I" || fullName?.StartsWith("I-") == true || fullName?.StartsWith("I ") == true)
            return RoadHierarchy.Interstate;

        // US Highways
        if (roadType == "U" || fullName?.Contains("US Hwy") == true || fullName?.Contains("US-") == true)
            return RoadHierarchy.USHighway;

        // State highways (includes FM roads in Texas)
        if (roadType == "S" || fullName?.StartsWith("FM ") == true || fullName?.StartsWith("State Hwy") == true)
            return RoadHierarchy.StateHighway;

        // Use MTFCC codes for classification
        if (mtfcc != null)
        {
            return mtfcc switch
            {
                "S1100" => RoadHierarchy.Interstate,    // Primary roads
                "S1200" => RoadHierarchy.USHighway,     // Secondary roads
                "S1400" => RoadHierarchy.Arterial,      // Local neighborhood roads
                "S1630" or "S1640" => RoadHierarchy.Ramp, // Ramp codes
                _ => RoadHierarchy.LocalRoad
            };
        }

        // Default classification
        if (roadType == "M") return RoadHierarchy.Arterial; // Major roads

        return RoadHierarchy.LocalRoad;
    }

    public string? ExtractRouteDesignation(GeoJsonFeature road)
    {
        var fullName = GetRoadName(road);
        if (string.IsNullOrEmpty(fullName))
            return null;

        // Extract route designations using regex patterns
        var patterns = new[]
        {
            @"I-?\s*(\d+)",           // I-20, I 20
            @"US\s+Hwy\s+(\d+)",      // US Hwy 180
            @"US-(\d+)",              // US-180
            @"FM\s+(\d+)",            // FM 5
            @"State\s+Hwy\s+(\d+)"    // State Hwy 199
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(fullName, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var routeType = pattern.Contains("I-") ? "I" :
                               pattern.Contains("US") ? "US" :
                               pattern.Contains("FM") ? "FM" : "SH";
                return $"{routeType}-{match.Groups[1].Value}";
            }
        }

        return null;
    }

    private bool IsMainlineRoad(RoadHierarchy hierarchy)
    {
        return hierarchy != RoadHierarchy.Ramp;
    }

    private double CalculateDistance(GeoJsonFeature road, EnhancedTrafficLocation trafficLocation)
    {
        // Get road geometry and find closest point to traffic location
        var roadCoords = ExtractRoadCoordinates(road);
        if (!roadCoords.Any())
            return double.MaxValue;

        var trafficLat = trafficLocation.Latitude;
        var trafficLon = trafficLocation.Longitude;

        // Find minimum distance to any point on the road
        return roadCoords.Min(coord =>
        {
            var roadLat = coord.Latitude;
            var roadLon = coord.Longitude;
            return Math.Sqrt(Math.Pow(roadLon - trafficLon, 2) + Math.Pow(roadLat - trafficLat, 2));
        });
    }

    private List<(double Latitude, double Longitude)> ExtractRoadCoordinates(GeoJsonFeature road)
    {
        var coordinates = new List<(double, double)>();

        if (road.Geometry is LineStringGeometry lineString)
        {
            coordinates.AddRange(lineString.Coordinates
                .Where(coord => coord.Count >= 2)
                .Select(coord => (coord[1], coord[0]))); // GeoJSON is [lon, lat]
        }
        else if (road.Geometry is MultiLineStringGeometry multiLineString)
        {
            foreach (var line in multiLineString.Coordinates)
            {
                coordinates.AddRange(line
                    .Where(coord => coord.Count >= 2)
                    .Select(coord => (coord[1], coord[0]))); // GeoJSON is [lon, lat]
            }
        }

        return coordinates;
    }

    private string GetRoadName(GeoJsonFeature road)
    {
        var properties = road.Properties ?? new Dictionary<string, object?>();
        return GetPropertyValue(properties, "FULLNAME") ?? "Unknown Road";
    }

    private string? GetPropertyValue(Dictionary<string, object?> properties, string key)
    {
        if (properties.TryGetValue(key, out var value))
        {
            return value?.ToString();
        }
        return null;
    }

    public List<EnhancedTrafficLocation> ConvertToEnhancedLocations(List<TrafficCountData> trafficData)
    {
        var enhancedLocations = new List<EnhancedTrafficLocation>();

        foreach (var record in trafficData)
        {
            if (record.LocationInfo?.Latitude == null || record.LocationInfo?.Longitude == null)
                continue;

            var latestAadt = record.AadtData?.OrderByDescending(a => a.Year).FirstOrDefault();

            var enhanced = new EnhancedTrafficLocation
            {
                LocationId = record.LocationInfo.LocationId ?? string.Empty,
                Latitude = (double)record.LocationInfo.Latitude,
                Longitude = (double)record.LocationInfo.Longitude,
                Aadt = latestAadt?.Aadt,
                Dhv30 = latestAadt?.Dhv30,
                AadtYear = latestAadt?.Year,
                LocatedOn = record.LocationInfo.LocatedOn,
                Category = record.LocationInfo.Category,
                RouteType = record.LocationInfo.RouteType,
                FunctionalClass = record.LocationInfo.FnctClass
            };

            // Classify the location type and target hierarchy
            ClassifyTrafficLocation(enhanced);

            enhancedLocations.Add(enhanced);
        }

        _logger.LogInformation("Converted {Count} traffic locations to enhanced format", enhancedLocations.Count);
        return enhancedLocations;
    }

    private void ClassifyTrafficLocation(EnhancedTrafficLocation location)
    {
        // Determine location type based on category
        location.LocationType = location.Category?.ToUpper() switch
        {
            "RAMP" => TrafficLocationType.Ramp,
            "MAINLINE" => DetermineMainlineType(location.FunctionalClass),
            _ => TrafficLocationType.Unknown
        };

        // Determine target road hierarchy
        location.TargetRoadHierarchy = DetermineHierarchyFromFunctionalClass(location.FunctionalClass);

        // Set mainline flag
        location.IsMainlineLocation = location.LocationType != TrafficLocationType.Ramp;

        // Extract route designation
        location.RouteDesignation = ExtractRouteFromLocation(location);
    }

    private TrafficLocationType DetermineMainlineType(string? functionalClass)
    {
        return functionalClass?.ToLower() switch
        {
            var fc when fc?.Contains("interstate") == true => TrafficLocationType.MainlineInterstate,
            var fc when fc?.Contains("highway") == true => TrafficLocationType.MainlineHighway,
            var fc when fc?.Contains("arterial") == true => TrafficLocationType.Arterial,
            _ => TrafficLocationType.Unknown
        };
    }

    private RoadHierarchy DetermineHierarchyFromFunctionalClass(string? functionalClass)
    {
        return functionalClass?.ToLower() switch
        {
            var fc when fc?.Contains("interstate") == true => RoadHierarchy.Interstate,
            var fc when fc?.Contains("principal arterial") == true => RoadHierarchy.USHighway,
            var fc when fc?.Contains("minor arterial") == true => RoadHierarchy.StateHighway,
            var fc when fc?.Contains("collector") == true => RoadHierarchy.Arterial,
            var fc when fc?.Contains("local") == true => RoadHierarchy.LocalRoad,
            _ => RoadHierarchy.LocalRoad
        };
    }

    private string? ExtractRouteFromLocation(EnhancedTrafficLocation location)
    {
        var locatedOn = location.LocatedOn;
        if (string.IsNullOrEmpty(locatedOn))
            return null;

        // Parse route from TxDOT format (e.g., "I020", "US0180", "FM0005", "FM0920")
        var routeMatch = Regex.Match(locatedOn, @"([A-Z]+)(\d+)");
        if (routeMatch.Success)
        {
            var routeType = routeMatch.Groups[1].Value;
            var routeNumber = routeMatch.Groups[2].Value.TrimStart('0');

            return routeType switch
            {
                "I" => $"I-{routeNumber}",
                "US" => $"US-{routeNumber}",
                "FM" => $"FM-{routeNumber}",
                "SH" => $"SH-{routeNumber}",
                _ => null
            };
        }

        return null;
    }

    /// <summary>
    /// Finds traffic locations where LocatedOn explicitly matches the target road's route.
    /// This is the highest priority match - if a location explicitly names a road, it should only match that road.
    /// </summary>
    private TrafficMatchResult? FindExplicitLocatedOnMatch(GeoJsonFeature road,
        List<EnhancedTrafficLocation> locations, string roadRoute, RoadHierarchy roadHierarchy)
    {
        // Find locations where LocatedOn explicitly matches this road's route
        var matches = locations
            .Where(l => !string.IsNullOrEmpty(l.LocatedOn))
            .Select(l => new
            {
                Location = l,
                LocatedOnRoute = ExtractRouteFromLocation(l),
                Distance = CalculateDistance(road, l)
            })
            .Where(m => m.LocatedOnRoute == roadRoute) // LocatedOn must match target road's route
            .Where(m => m.Distance <= LOOSE_BUFFER) // Must be within reasonable distance
            .OrderBy(m => m.Distance)
            .ToList();

        if (!matches.Any())
            return null;

        var bestMatch = matches.First();
        return new TrafficMatchResult
        {
            IsMatch = true,
            MatchedLocation = bestMatch.Location,
            Distance = bestMatch.Distance,
            MatchType = "ExplicitLocatedOn",
            SourceType = bestMatch.Location.LocationType
        };
    }

    /// <summary>
    /// Checks if a location's LocatedOn value conflicts with the target road's route.
    /// Returns true if the location explicitly names a different road (conflict).
    /// Returns false if LocatedOn is empty, matches the target, or doesn't contain a parseable route.
    /// 
    /// KEY RULE: If a location has LocatedOn that parses to a specific route (e.g., "FM0920" → "FM-920"),
    /// it should ONLY match roads with that exact route designation. Roads without route designations
    /// are considered a conflict (they're not the explicitly named road).
    /// </summary>
    private bool HasConflictingLocatedOn(EnhancedTrafficLocation location, string? targetRoadRoute)
    {
        // If no LocatedOn specified, no conflict
        if (string.IsNullOrEmpty(location.LocatedOn))
            return false;

        // Parse route from LocatedOn
        var locatedOnRoute = ExtractRouteFromLocation(location);
        
        // If can't parse LocatedOn, no conflict (might be a street name, not a route)
        if (string.IsNullOrEmpty(locatedOnRoute))
            return false;

        // CRITICAL: If location has a route-specific LocatedOn, it must match exactly
        // If target road has no route designation, it's a conflict (location names a specific route)
        if (string.IsNullOrEmpty(targetRoadRoute))
        {
            return true; // Conflict: location names specific route, but target road has no route
        }

        // Conflict if LocatedOn route doesn't match target road route
        return locatedOnRoute != targetRoadRoute;
    }
}