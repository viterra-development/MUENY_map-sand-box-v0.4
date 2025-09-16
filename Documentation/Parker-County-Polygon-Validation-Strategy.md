# Parker County Polygon Validation Strategy

## Current Problem

The existing rectangular bounds validation is too restrictive and incorrectly filters out 198 crashes (11.3% of the dataset):

```csharp
// Current rough rectangular bounds
MinLatitude = 32.5m,   MaxLatitude = 33.0m,
MinLongitude = -98.0m, MaxLongitude = -97.0m
```

**Issue**: Counties have irregular shapes, not rectangles. The actual Parker County extends to longitude -98.06, causing valid crashes to be rejected.

## Solution Strategy

### 1. **Fetch Actual Parker County Polygon**
- **Source**: US Census TIGER web service
- **URL**: `https://tigerweb.geo.census.gov/arcgis/rest/services/TIGERweb/tigerWMS_Current/MapServer/82/query?where=STATE%3D%2748%27%20AND%20COUNTY%3D%27367%27&outFields=*&returnGeometry=true&outSR=4326&f=geojson`
- **Format**: GeoJSON with actual county boundary polygon
- **FIPS Code**: 48367 (Texas = 48, Parker County = 367)

### 2. **Implementation Approach**

#### **Option A: Static Polygon (Recommended)**
Store the polygon coordinates directly in code for performance:

```csharp
private static readonly Polygon _parkerCountyPolygon = CreateParkerCountyPolygon();

private static Polygon CreateParkerCountyPolygon()
{
    var coordinates = new Coordinate[]
    {
        // Actual Parker County boundary coordinates from Census
        new Coordinate(-98.060338000212965, 32.810695999793673),
        new Coordinate(-97.xxx, 32.xxx),
        // ... all boundary points
        new Coordinate(-98.060338000212965, 32.810695999793673) // Close polygon
    };
    var geometryFactory = new GeometryFactory();
    var shell = geometryFactory.CreateLinearRing(coordinates);
    return geometryFactory.CreatePolygon(shell);
}
```

#### **Option B: Dynamic Loading (Alternative)**
Fetch polygon from Census API at startup:

```csharp
private Polygon? _parkerCountyPolygon;

public async Task<Polygon> LoadParkerCountyPolygonAsync()
{
    if (_parkerCountyPolygon != null) return _parkerCountyPolygon;

    var httpClient = new HttpClient();
    var response = await httpClient.GetStringAsync(CENSUS_URL);
    var geoJson = JsonDocument.Parse(response);
    // Parse GeoJSON and convert to NetTopologySuite Polygon
    return _parkerCountyPolygon;
}
```

### 3. **Validation Implementation**

Replace the rectangular bounds check with point-in-polygon:

```csharp
private bool IsWithinParkerCounty(decimal latitude, decimal longitude)
{
    var point = new Point(new Coordinate((double)longitude, (double)latitude));
    return _parkerCountyPolygon.Contains(point);
}
```

### 4. **Benefits of This Approach**

#### **Accuracy**
- ✅ **Precise geographical validation** using actual county boundaries
- ✅ **Eliminates false negatives** from rectangular approximation
- ✅ **Proper handling of irregular county shape**

#### **Performance**
- ✅ **Fast point-in-polygon operations** with NetTopologySuite
- ✅ **No network calls during processing** (Option A)
- ✅ **Minimal memory overhead** for polygon storage

#### **Maintainability**
- ✅ **Uses authoritative government data** (US Census)
- ✅ **Standard GeoJSON format** for interoperability
- ✅ **Clear validation logic** with geometric operations

### 5. **Expected Impact**

#### **Before (Rectangular Bounds)**
- 1,760 total crashes
- 198 filtered out (11.3%)
- 1,562 valid crashes

#### **After (Polygon Validation)**
- 1,760 total crashes
- ~20-30 filtered out (1-2%) - only truly outside county
- ~1,730-1,740 valid crashes

**Expected improvement**: ~170-180 additional valid crashes recovered

### 6. **Implementation Steps**

1. **Fetch polygon data** from Census TIGER service
2. **Add NetTopologySuite dependencies** (already present)
3. **Create polygon geometry** from GeoJSON coordinates
4. **Replace IsWithinParkerCounty method** with point-in-polygon check
5. **Test validation** with known Parker County locations
6. **Verify crash recovery** - expect ~170+ additional valid crashes

### 7. **Code Changes Required**

#### **CrisCsvParser.cs Updates**
```csharp
// Add imports
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

// Replace _parkerCountyBounds with _parkerCountyPolygon
private static readonly Polygon _parkerCountyPolygon = CreateParkerCountyPolygon();

// Update validation method
private bool IsWithinParkerCounty(decimal latitude, decimal longitude)
{
    var point = new GeometryFactory().CreatePoint(
        new Coordinate((double)longitude, (double)latitude)
    );
    return _parkerCountyPolygon.Contains(point);
}
```

### 8. **Validation Testing**

Test with known Parker County locations:
- **Weatherford, TX** (county seat): 32.7593°N, 97.7970°W
- **Aledo, TX**: 32.6962°N, 97.6022°W
- **Azle, TX**: 32.8945°N, 97.5464°W

All should validate as `true` with the new polygon method.

### 9. **Fallback Strategy**

If polygon validation fails for any reason:
- Log warning message
- Fall back to expanded rectangular bounds:
  ```csharp
  MinLatitude = 32.4m,   MaxLatitude = 33.1m,
  MinLongitude = -98.1m, MaxLongitude = -96.9m
  ```

## Conclusion

This strategy will provide **accurate, authoritative validation** using the actual Parker County boundary polygon, recovering approximately **170+ additional valid crashes** that were incorrectly filtered by the rectangular approximation.

The implementation uses industry-standard geometric libraries and government data sources for maximum reliability and precision.