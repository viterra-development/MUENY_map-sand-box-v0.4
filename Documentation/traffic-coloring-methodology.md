## Methodology: Color Matching for Traffic Stations and Traffic-Road Gradient

### Purpose
Explain how traffic count station points are colored and sized, how roadway segments receive traffic attributes, and how those attributes drive the traffic road gradient styling. Also highlight the relationship and current gaps between the point (station) color scheme and the line (road) gradient.

### Data production pipeline (relevant parts)
- **Traffic stations (points)** are scraped and consolidated in `TCDS.Importer`, then exported both as full GeoJSON and as tiles for fast client loading.
  - Tiles are generated at `/tiles/traffic-counts/{z}/{x}/{y}.geojson` for zoom 12–16. Each feature includes `latestAadt`, `latestAadtYear`, `active`, `locatedOn`, `fnctClass`, and other attributes.
  - Source: `TCDS.Importer/Services/SimpleTileGenerator.cs`
- **Roads with traffic** are produced by spatially matching traffic stations to roads within ~100 m, then embedding a `traffic` object on the matched road feature (`aadt`, `aadtYear`, `locationId`, `locatedOn`, etc.). Output is `MapSandBox/wwwroot/parker-roads-with-traffic.geojson`.
  - Source: `TCDS.Importer/Services/RoadTrafficMerger.cs`

### How traffic station points are styled (color and size)
- Implemented in `MapSandBox/wwwroot/js/maplibre-deckgl-integration.js` via a `TileLayer` → `GeoJsonLayer` sublayer.
- Color rule uses discrete bins based on `feature.properties.latestAadt`, with a gray override for inactive stations:
  - **Inactive** (`active == 'No'`): `[128, 128, 128, 180]`
  - **No AADT**: `[200, 200, 200, 180]`
  - **AADT ≥ 5000**: `[255, 0, 0, 200]` (Red)
  - **1000 ≤ AADT < 5000**: `[255, 165, 0, 200]` (Orange)
  - **500 ≤ AADT < 1000**: `[255, 255, 0, 200]` (Yellow)
  - **100 ≤ AADT < 500**: `[0, 255, 0, 200]` (Green)
  - **AADT < 100**: `[0, 0, 255, 200]` (Blue)
- Size rule uses a logarithmic scale mapped to pixel radius, with a debug minimum enforced in the TileLayer:
  - Base function maps AADT range roughly 90–8,400 using `log(aadt)` to `radius` ≈ 5–25 px.
  - In the TileLayer sublayer, the returned radius is clamped to a minimum of 12 px for visibility.

Code references:

```428:453:MapSandBox/wwwroot/js/maplibre-deckgl-integration.js
function getTrafficColor(feature) {
    const aadt = feature.properties.latestAadt;
    const active = feature.properties.active;
    
    // Different colors for inactive locations
    if (active === 'No') {
        return [128, 128, 128, 180]; // Gray for inactive
    }
    
    if (!aadt) {
        return [200, 200, 200, 180]; // Light gray for no data
    }
    
    // Color scale based on AADT value (traffic volume)
    if (aadt >= 5000) {
        return [255, 0, 0, 200];     // Red for high traffic (5000+)
    } else if (aadt >= 1000) {
        return [255, 165, 0, 200];   // Orange for medium-high traffic (1000-5000)
    } else if (aadt >= 500) {
        return [255, 255, 0, 200];   // Yellow for medium traffic (500-1000)
    } else if (aadt >= 100) {
        return [0, 255, 0, 200];     // Green for low traffic (100-500)
    } else {
        return [0, 0, 255, 200];     // Blue for very low traffic (<100)
    }
}
```

```411:426:MapSandBox/wwwroot/js/maplibre-deckgl-integration.js
function getTrafficRadius(feature) {
    const aadt = feature.properties.latestAadt;
    if (!aadt) return 5; // Default size for no data
    
    // Scale radius based on AADT value (logarithmic scale)
    // AADT ranges from ~90 to ~8400 in the data
    const minRadius = 5;
    const maxRadius = 25;
    const logAadt = Math.log(Math.max(aadt, 1));
    const logMin = Math.log(90);
    const logMax = Math.log(8400);
    
    const normalizedValue = (logAadt - logMin) / (logMax - logMin);
    return minRadius + (maxRadius - minRadius) * Math.max(0, Math.min(1, normalizedValue));
}
```

```335:341:MapSandBox/wwwroot/js/maplibre-deckgl-integration.js
getPointRadius: (feature) => {
    const radius = getTrafficRadius(feature);
    console.log(`Point radius for ${feature.properties.locationId}: ${radius}`);
    return Math.max(radius, 12); // Ensure minimum 12px for debugging
},
```

### How roadway segments receive traffic data
- Matching logic: for each road line (LineString or MultiLineString), find the first station point whose coordinate is within `BUFFER_DISTANCE = 0.001` degrees (~100 m) of any coordinate in the road geometry. If found, copy the latest station metrics into `road.properties.traffic` and stop at first match.
- Embedded properties on roads: `traffic.aadt`, `traffic.dhv30`, `traffic.aadtYear`, `traffic.locationId`, `traffic.locatedOn`.
- Output: FeatureCollection with only roads that matched any station.

Code references:

```71:118:TCDS.Importer/Services/RoadTrafficMerger.cs
// ... existing code ...
foreach (var trafficLocation in trafficData.Features)
{
    if (RoadIntersectsTrafficBuffer(road, trafficLocation, BUFFER_DISTANCE))
    {
        hasTrafficData = true;
        trafficProperties = new TrafficProperties
        {
            Aadt = GetJsonValue<int?>(props, "latestAadt"),
            Dhv30 = GetJsonValue<int?>(props, "latestDhv30"),
            AadtYear = GetJsonValue<int?>(props, "latestAadtYear"),
            LocationId = GetJsonValue<string>(props, "locationId"),
            LocatedOn = GetJsonValue<string>(props, "locatedOn")
        };
        break; // first match only
    }
}
// ... existing code ...
```

```423:470:TCDS.Importer/Services/RoadTrafficMerger.cs
// ... existing code ...
private bool RoadIntersectsTrafficBuffer(GeoJsonFeature road, GeoJsonFeature trafficLocation, double bufferDistance)
{
    // Haversine approximation in degree space (euclidean on lon/lat) with threshold ~0.001°
    // Returns true if any road vertex is within the buffer of the station point
}
// ... existing code ...
```

### How traffic roads are styled (color and width)
- Implemented as a `PathLayer` for config id `parker-roads-traffic` in `maplibre-deckgl-integration.js`.
- Color is a continuous log-scale gradient derived from `road.properties.traffic.aadt`:
  - Domain: `log10(aadt)` mapped from `[minLog=1.0, maxLog=5.2]` → `[0,1]` ratio
  - Color: `[red = 255*ratio, green = 255*(1-ratio), 0, 180]` which smoothly transitions Green → Yellow → Red as AADT increases
- Width is also log-scaled:
  - Domain: same `[minLog=1.0, maxLog=5.2]`
  - Width: `2 + ratio*8` px (range 2–10 px)

Code references:

```486:516:MapSandBox/wwwroot/js/maplibre-deckgl-integration.js
// ... existing code ...
function getTrafficGradientColor(feature) {
    const aadt = feature.properties.traffic?.aadt || 0;
    if (aadt < 10) return [120, 120, 120, 128];
    const logAADT = Math.log10(aadt);
    const minLog = 1.0;
    const maxLog = 5.2;
    const ratio = Math.min(Math.max((logAADT - minLog) / (maxLog - minLog), 0), 1);
    const red = Math.floor(255 * ratio);
    const green = Math.floor(255 * (1 - ratio));
    return [red, green, 0, 180];
}
function getTrafficWidth(feature) {
    const aadt = feature.properties.traffic?.aadt || 0;
    if (aadt < 10) return 1;
    const logAADT = Math.log10(aadt);
    const minLog = 1.0;
    const maxLog = 5.2;
    const ratio = Math.min(Math.max((logAADT - minLog) / (maxLog - minLog), 0), 1);
    return 2 + (ratio * 8);
}
// ... existing code ...
```

### Relationship between station colors and road gradient
- **Data linkage**: A road’s color and width come from the station it matched during merge. Therefore, a station’s AADT should conceptually correlate with the color and width of the nearby road segment.
- **Scheme difference**:
  - Stations use a **discrete, binned** scheme (Blue → Green → Yellow → Orange → Red) with hard thresholds and an `active` gray override.
  - Roads use a **continuous, log-scale gradient** (Green → Yellow → Red) across a fixed domain `[10^1, 10^5.2]`.
- **Domain mismatch risk**:
  - Station logic implicitly targets Parker County’s current observed range (~90–8,400 AADT) for sizing; color thresholds also live in that magnitude.
  - Road gradient is calibrated for a much wider potential range (up to ~158,869). For local Parker values, this can compress the ratio toward the green–yellow portion, potentially making roads appear greener than the station’s discrete color might suggest for the same location.
- **Result**: Close-by station and road may not visually “match” hue one-to-one due to different scaling strategies and domains, even though they derive from the same AADT.

### Known constraints and edge cases
- First-match strategy: if multiple stations are close to a road, only the first encountered match is used; no averaging or closest-distance choice.
- Vertex-only proximity: matching checks only vertex distances to the station point; it does not compute true line-to-point distance or segment snapping.
- No segmentation: roads are not split into subsegments by station influence; the entire feature gets one `traffic` object.
- Fixed gradient domain: roads use fixed `[1.0, 5.2]` log10 bounds rather than dataset-driven min/max, which may reduce contrast for smaller local ranges.
- Station color bins are fixed and do not adapt based on observed distribution.

### Recommendations to align station colors with road gradient (optional improvements)
- Unify domains:
  - Option A: Use the same min/max (or quantiles) to define both road gradient and station sizing/color scale.
  - Option B: Drive both from dataset metadata (e.g., compute min/max log10(AADT) during merge and store in output metadata).
- Harmonize palettes:
  - Either discretize the road gradient to the same bins as stations, or use a continuous palette for stations derived from the same gradient function.
  - Keep the inactive gray override for stations; optionally reduce alpha for inactive to indicate non-applicability.
- Improve matching fidelity:
  - Use closest distance from point to polyline, not vertex-only proximity.
  - Prefer the nearest station within the buffer, possibly with a tighter buffer at city scale (e.g., 30–50 m).
  - Optionally segment roads and interpolate where multiple stations exist along a corridor.
- Externalize scales and thresholds into configuration to make adjustments data-driven without code edits.

### Where the data comes from in tiles
```250:260:MapSandBox/wwwroot/js/maplibre-deckgl-integration.js
const tileLayer = new deck.TileLayer({
    id: config.id,
    data: '/tiles/traffic-counts/{z}/{x}/{y}.geojson',
    minZoom: 12,
    maxZoom: 16,
    tileSize: 512,
    // ...
});
```

```139:191:TCDS.Importer/Services/SimpleTileGenerator.cs
// Convert to GeoJSON features
var features = records.Select(record =>
{
    // Get latest AADT value
    var latestAadt = record.AadtData
        .Where(a => a.Aadt.HasValue)
        .OrderByDescending(a => a.Year)
        .FirstOrDefault();
    // Get latest volume count
    var latestVolumeCount = record.VolumeCountData
        .OrderByDescending(v => v.Date)
        .FirstOrDefault();
    return new
    {
        type = "Feature",
        geometry = new { type = "Point", coordinates = new[] { (double)record.LocationInfo.Longitude!.Value, (double)record.LocationInfo.Latitude!.Value } },
        properties = new
        {
            locationId = record.LocationId,
            locatedOn = record.LocationInfo.LocatedOn,
            // ...
            latestAadt = latestAadt?.Aadt,
            latestAadtYear = latestAadt?.Year,
            latestDhv30 = latestAadt?.Dhv30,
            // ...
        }
    };
}).ToList();
```

```200:209:TCDS.Importer/Services/SimpleTileGenerator.cs
// Save tile file
var tileFilePath = Path.Combine(xPath, $"{y}.geojson");
var jsonOptions = new JsonSerializerOptions { WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
var jsonData = JsonSerializer.Serialize(geoJson, jsonOptions);
await File.WriteAllTextAsync(tileFilePath, jsonData);
```

### Summary
- Stations: discrete, threshold-based colors + log-scaled size; inactive stations gray.
- Roads: continuous log-gradient [Green → Yellow → Red] for color and 2–10 px for width, both from `traffic.aadt` embedded by spatial matching.
- Relationship: roads inherit AADT from nearby stations; visual mismatch can occur due to different scaling domains and discrete vs. continuous mapping.
- Recommendation: unify domain and palette, and consider improving spatial matching and configuration for robust, consistent visuals.

