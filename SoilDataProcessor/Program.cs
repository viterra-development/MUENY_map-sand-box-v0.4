using System.Text.Json;
using NetTopologySuite.IO;
using SoilDataProcessor;
using MapSandBox.Shared.Models;

class Program
{
    private static readonly HttpClient _httpClient = new();
    private const string SSURGO_API_BASE = "https://SDMDataAccess.sc.egov.usda.gov/Tabular/post.rest";
    
    static async Task Main(string[] args)
    {
        Console.WriteLine("SSURGO Soil Data Processor");
        Console.WriteLine("==========================");
        
        try
        {
            Console.WriteLine("1. Testing real SSURGO API with small sample area...");
            Console.WriteLine("   (Querying actual USDA data)");

            // Create small test area in Parker County (approximately 1 km²)
            var testAreaWkt = "POLYGON((-97.800 32.750, -97.790 32.750, -97.790 32.760, -97.800 32.760, -97.800 32.750))";

            // Query real SSURGO data
            var ssurgoResponse = await QuerySSURGOApiAsync(testAreaWkt);

            // Debug: Show what we got from the API
            Console.WriteLine($"   API Response: {ssurgoResponse.Substring(0, Math.Min(200, ssurgoResponse.Length))}...");

            var geoJsonFeatures = await ConvertSSURGOToGeoJSONWithGeometry(ssurgoResponse);

            // Fallback to sample data if API fails
            if (geoJsonFeatures.Count == 0)
            {
                Console.WriteLine("   No data returned from API, falling back to sample data...");
                geoJsonFeatures = CreateSampleSoilData();
            }
            
            Console.WriteLine($"2. Created {geoJsonFeatures.Count} sample soil map units");
            
            // Create output directory
            var outputDir = Path.Combine("..", "MapSandBox", "wwwroot", "soil-data");
            Directory.CreateDirectory(outputDir);
            
            Console.WriteLine("3. Creating GeoJSON files...");
            
            // Create different GeoJSON files for different visualizations
            var combinedGeoJson = CreateGeoJsonCollection(geoJsonFeatures);
            var clayVisualizationGeoJson = CreateClayVisualizationGeoJson(geoJsonFeatures);
            var ksatVisualizationGeoJson = CreateKsatVisualizationGeoJson(geoJsonFeatures);
            
            // Write files
            await File.WriteAllTextAsync(Path.Combine(outputDir, "parker-county-test-combined.geojson"), combinedGeoJson);
            await File.WriteAllTextAsync(Path.Combine(outputDir, "parker-county-test-clay.geojson"), clayVisualizationGeoJson);
            await File.WriteAllTextAsync(Path.Combine(outputDir, "parker-county-test-ksat.geojson"), ksatVisualizationGeoJson);
            
            Console.WriteLine("✅ Successfully generated soil data files:");
            Console.WriteLine($"   - {Path.Combine(outputDir, "parker-county-test-combined.geojson")}");
            Console.WriteLine($"   - {Path.Combine(outputDir, "parker-county-test-clay.geojson")}");
            Console.WriteLine($"   - {Path.Combine(outputDir, "parker-county-test-ksat.geojson")}");
            
            Console.WriteLine("\n4. Summary of processed data:");
            foreach (var feature in geoJsonFeatures.Take(3))
            {
                var props = feature.Properties;
                Console.WriteLine($"   Map Unit: {props.MuSym} - {props.MuName}");
                Console.WriteLine($"   Clay: {props.SoilClayPct}%, Ksat: {props.SoilKsatUmPerS} μm/s");
            }
            if (geoJsonFeatures.Count > 3)
            {
                Console.WriteLine($"   ... and {geoJsonFeatures.Count - 3} more units");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
    
    private static async Task<string> QuerySSURGOApiAsync(string areaGeometry)
    {
        var query = BuildVectorSoilDataQuery(areaGeometry);
        
        var requestBody = new { query = query, format = "json" };
        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        
        Console.WriteLine("   Querying SSURGO API...");
        
        var response = await _httpClient.PostAsync(SSURGO_API_BASE, content);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"   API Error ({response.StatusCode}): {errorContent}");
            throw new HttpRequestException($"SSURGO API returned {response.StatusCode}: {errorContent}");
        }
        
        var responseContent = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"   Received {responseContent.Length} characters from SSURGO API");
        
        return responseContent;
    }

    private static async Task<string> GetSoilGeometriesAsync(List<string> mukeys)
    {
        if (mukeys.Count == 0)
        {
            return "{\"Table\":[]}";
        }

        var mukeyList = string.Join("','", mukeys);
        var geometryQuery = $@"
            SELECT
                mapunit.mukey,
                G.MupolygonWktWgs84 as geom
            FROM mapunit
            CROSS APPLY SDA_Get_MupolygonWktWgs84_from_Mukey(mapunit.mukey) as G
            WHERE mapunit.mukey IN ('{mukeyList}')";

        var requestBody = new { query = geometryQuery, format = "json" };
        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        Console.WriteLine($"   Querying SSURGO geometry API for {mukeys.Count} map units...");

        var response = await _httpClient.PostAsync(SSURGO_API_BASE, content);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"   Geometry API Error ({response.StatusCode}): {errorContent}");
            throw new HttpRequestException($"SSURGO Geometry API returned {response.StatusCode}: {errorContent}");
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"   Received {responseContent.Length} characters from geometry API");

        return responseContent;
    }

    private static double CalculateWeightedAverage<T>(IGrouping<string, T> group,
        Func<T, double?> valueSelector, Func<T, double?> weightSelector)
    {
        var validData = group.Where(r => valueSelector(r).HasValue && weightSelector(r).HasValue).ToList();
        if (!validData.Any()) return 0;

        var weightedSum = validData.Sum(r => valueSelector(r)!.Value * weightSelector(r)!.Value);
        var totalWeight = validData.Sum(r => weightSelector(r)!.Value);

        return totalWeight > 0 ? weightedSum / totalWeight : 0;
    }

    private static async Task<List<GeoJsonFeature<SoilProperties>>> ConvertSSURGOToGeoJSONWithGeometry(string ssurgoResponse)
    {
        // First, get soil properties using existing method
        var propertiesFeatures = ConvertSSURGOToGeoJSON(ssurgoResponse);

        if (propertiesFeatures.Count == 0)
        {
            return propertiesFeatures;
        }

        // Extract the mukeys to get geometries for
        var mukeys = propertiesFeatures.Select(f => f.Properties.MuKey).ToList();

        try
        {
            // Get real geometries for these mukeys
            var geometryResponse = await GetSoilGeometriesAsync(mukeys);
            var geometryData = JsonSerializer.Deserialize<SsurgoApiResponse>(geometryResponse);

            if (geometryData?.Table != null && geometryData.Table.Count > 0)
            {
                Console.WriteLine($"   Retrieved geometry for {geometryData.Table.Count} map units");

                // Group geometries by mukey (each mukey can have multiple polygons)
                var geometryGroups = geometryData.Table
                    .Where(row => row.Length >= 2)
                    .GroupBy(row => row[0]) // Group by mukey
                    .ToDictionary(g => g.Key, g => g.Select(row => row[1]).ToList()); // mukey -> List<WKT>

                // Update features with real geometries
                foreach (var feature in propertiesFeatures)
                {
                    var mukey = feature.Properties.MuKey;
                    if (geometryGroups.TryGetValue(mukey, out var wktGeometries) && wktGeometries.Count > 0)
                    {
                        try
                        {
                            if (wktGeometries.Count == 1)
                            {
                                // Single polygon - convert directly
                                feature.Geometry = ConvertWktToGeoJsonGeometry(wktGeometries[0]);
                            }
                            else
                            {
                                // Multiple polygons - create MultiPolygon or use first one for now
                                Console.WriteLine($"   Note: Map unit {mukey} has {wktGeometries.Count} polygons, using first one");
                                feature.Geometry = ConvertWktToGeoJsonGeometry(wktGeometries[0]);
                            }
                            feature.Properties.Note = "Real SSURGO data with actual geometry";
                            feature.Properties.PolygonCount = wktGeometries.Count;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"   Warning: Failed to convert geometry for mukey {mukey}: {ex.Message}");
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine("   Warning: No geometry data returned, keeping placeholder geometry");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Warning: Failed to retrieve geometries: {ex.Message}");
            Console.WriteLine("   Continuing with placeholder geometry...");
        }

        return propertiesFeatures;
    }

    private static string BuildVectorSoilDataQuery(string areaGeometry)
    {
        // SSURGO doesn't directly provide geometry in tabular queries
        // We need to get the data and then fetch geometry separately
        return $@"
            SELECT
                mu.mukey,
                mu.musym,
                mu.muname,
                co.cokey,
                co.compname,
                co.comppct_r as component_pct,
                ch.claytotal_r as soil_clay_pct,
                ch.ksat_r as soil_ksat_um_per_s
            FROM mapunit mu
            INNER JOIN component co ON mu.mukey = co.mukey
            INNER JOIN chorizon ch ON co.cokey = ch.cokey
            WHERE mu.mukey IN (
                SELECT mukey FROM SDA_Get_Mukey_from_intersection_with_WktWgs84('{areaGeometry}')
            )
            AND co.comppct_r >= 25
            AND ch.hzdept_r = 0
            ORDER BY mu.mukey, co.comppct_r DESC";
    }
    
    private static List<GeoJsonFeature<SoilProperties>> ConvertSSURGOToGeoJSON(string ssurgoResponse)
    {
        var ssurgoData = JsonSerializer.Deserialize<SsurgoApiResponse>(ssurgoResponse);
        if (ssurgoData?.Table == null || ssurgoData.Table.Count == 0)
        {
            return new List<GeoJsonFeature<SoilProperties>>();
        }

        Console.WriteLine($"   Received {ssurgoData.Table.Count} soil component records");

        // Convert array data to structured records
        // Array format: [mukey, musym, muname, cokey, compname, component_pct, soil_clay_pct, soil_ksat_um_per_s]
        var records = ssurgoData.Table
            .Where(row => row.Length >= 8)
            .Select(row => new
            {
                MuKey = row[0],
                MuSym = row[1],
                MuName = row[2],
                CoKey = row[3],
                CompName = row[4],
                ComponentPct = double.TryParse(row[5], out var pct) ? (double?)pct : null,
                ClayPct = double.TryParse(row[6], out var clay) ? (double?)clay : null,
                Ksat = double.TryParse(row[7], out var ksat) ? (double?)ksat : null
            })
            .ToList();

        Console.WriteLine($"   Parsed {records.Count} valid records");

        // Group by map unit and aggregate component data
        var groupedData = records
            .GroupBy(r => r.MuKey)
            .Select(g => new
            {
                MuKey = g.Key,
                MuSym = g.First().MuSym,
                MuName = g.First().MuName,
                // Weight-averaged soil properties by component percentage
                ClayPct = CalculateWeightedAverage(g, r => r.ClayPct, r => r.ComponentPct),
                Ksat = CalculateWeightedAverage(g, r => r.Ksat, r => r.ComponentPct)
            })
            .ToList();

        Console.WriteLine($"   Grouped into {groupedData.Count} unique map units");

        // Note: SSURGO tabular API doesn't return geometry directly
        // For now, create simple polygon features that will need geometry from spatial API
        var features = new List<GeoJsonFeature<SoilProperties>>();

        foreach (var mapUnit in groupedData)
        {
            // Create a placeholder polygon geometry
            var placeholderGeometry = new PolygonGeometry
            {
                Coordinates = new List<List<List<double>>>
                {
                    new List<List<double>>
                    {
                        new List<double> { -97.795, 32.755 },
                        new List<double> { -97.790, 32.755 },
                        new List<double> { -97.790, 32.760 },
                        new List<double> { -97.795, 32.760 },
                        new List<double> { -97.795, 32.755 }
                    }
                }
            };

            features.Add(new GeoJsonFeature<SoilProperties>
            {
                Properties = new SoilProperties
                {
                    MuKey = mapUnit.MuKey,
                    MuSym = mapUnit.MuSym,
                    MuName = mapUnit.MuName ?? "",
                    SoilClayPct = Math.Round(mapUnit.ClayPct, 1),
                    SoilKsatUmPerS = Math.Round(mapUnit.Ksat, 3),
                    Note = "Real SSURGO data with placeholder geometry"
                },
                Geometry = placeholderGeometry
            });
        }

        return features;
    }
    
    private static GeoJsonGeometry ConvertWktToGeoJsonGeometry(string wkt)
    {
        try
        {
            // Use NetTopologySuite for robust WKT to GeoJSON conversion
            var wktReader = new WKTReader();
            var geometry = wktReader.Read(wkt);

            // Convert to GeoJSON format
            var geoJsonWriter = new GeoJsonWriter();
            var geoJsonString = geoJsonWriter.Write(geometry);

            // Parse the geometry and deserialize to the correct type
            var geoJsonObject = JsonSerializer.Deserialize<JsonElement>(geoJsonString);

            // Extract the geometry part if it's wrapped in a feature
            JsonElement geometryElement = geoJsonObject.TryGetProperty("geometry", out var geomProp)
                ? geomProp
                : geoJsonObject;

            // Check the geometry type and deserialize accordingly
            if (geometryElement.TryGetProperty("type", out var typeProperty))
            {
                string geometryType = typeProperty.GetString()!;
                return geometryType switch
                {
                    "Polygon" => geometryElement.Deserialize<PolygonGeometry>()!,
                    "MultiPolygon" => geometryElement.Deserialize<MultiPolygonGeometry>()!,
                    "Point" => geometryElement.Deserialize<PointGeometry>()!,
                    "LineString" => geometryElement.Deserialize<LineStringGeometry>()!,
                    "MultiLineString" => geometryElement.Deserialize<MultiLineStringGeometry>()!,
                    _ => throw new NotSupportedException($"Geometry type '{geometryType}' is not supported")
                };
            }

            throw new InvalidOperationException("Geometry does not have a 'type' property");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Error converting WKT to GeoJSON: {ex.Message}");
            throw;
        }
    }
    
    private static string CreateGeoJsonCollection(List<GeoJsonFeature<SoilProperties>> features)
    {
        var collection = new GeoJsonFeatureCollection<SoilProperties> { Features = features };
        return JsonSerializer.Serialize(collection, new JsonSerializerOptions { WriteIndented = false });
    }
    
    private static string CreateClayVisualizationGeoJson(List<GeoJsonFeature<SoilProperties>> features)
    {
        // Create version optimized for clay percentage visualization
        var clayFeatures = features.Select(f => new GeoJsonFeature<ClayVisualizationProperties>
        {
            Geometry = f.Geometry,
            Properties = new ClayVisualizationProperties
            {
                MuKey = f.Properties.MuKey,
                MuSym = f.Properties.MuSym,
                MuName = f.Properties.MuName,
                SoilClayPct = f.Properties.SoilClayPct,
                Visualization = "clay"
            }
        }).ToList();

        var collection = new GeoJsonFeatureCollection<ClayVisualizationProperties> { Features = clayFeatures };
        return JsonSerializer.Serialize(collection, new JsonSerializerOptions { WriteIndented = false });
    }
    
    private static string CreateKsatVisualizationGeoJson(List<GeoJsonFeature<SoilProperties>> features)
    {
        // Create version optimized for permeability visualization
        var ksatFeatures = features.Select(f => new GeoJsonFeature<KsatVisualizationProperties>
        {
            Geometry = f.Geometry,
            Properties = new KsatVisualizationProperties
            {
                MuKey = f.Properties.MuKey,
                MuSym = f.Properties.MuSym,
                MuName = f.Properties.MuName,
                SoilKsatUmPerS = f.Properties.SoilKsatUmPerS,
                Visualization = "ksat"
            }
        }).ToList();

        var collection = new GeoJsonFeatureCollection<KsatVisualizationProperties> { Features = ksatFeatures };
        return JsonSerializer.Serialize(collection, new JsonSerializerOptions { WriteIndented = false });
    }
    
    private static List<GeoJsonFeature<SoilProperties>> CreateSampleSoilData()
    {
        // Create representative soil map units for Parker County, TX area
        // Using realistic soil types and properties found in North Texas
        var features = new List<GeoJsonFeature<SoilProperties>>();
        
        // Sample area coordinates around Parker County
        var sampleUnits = new[]
        {
            new { musym = "AlB", muname = "Altoga clay", clay = 45.2, ksat = 0.12, coords = new[] { -97.795, 32.755, -97.790, 32.755, -97.790, 32.760, -97.795, 32.760, -97.795, 32.755 } },
            new { musym = "DeC2", muname = "Denton clay", clay = 52.8, ksat = 0.08, coords = new[] { -97.790, 32.755, -97.785, 32.755, -97.785, 32.760, -97.790, 32.760, -97.790, 32.755 } },
            new { musym = "StB", muname = "Stephen clay loam", clay = 28.4, ksat = 1.45, coords = new[] { -97.795, 32.750, -97.790, 32.750, -97.790, 32.755, -97.795, 32.755, -97.795, 32.750 } },
            new { musym = "BoB2", muname = "Bolar clay loam", clay = 32.1, ksat = 0.92, coords = new[] { -97.790, 32.750, -97.785, 32.750, -97.785, 32.755, -97.790, 32.755, -97.790, 32.750 } },
            new { musym = "WeC", muname = "Weatherford fine sandy loam", clay = 18.7, ksat = 3.24, coords = new[] { -97.800, 32.755, -97.795, 32.755, -97.795, 32.760, -97.800, 32.760, -97.800, 32.755 } }
        };
        
        foreach (var unit in sampleUnits)
        {
            // Create polygon geometry coordinates
            var ring = new List<List<double>>();

            for (int i = 0; i < unit.coords.Length; i += 2)
            {
                ring.Add(new List<double> { unit.coords[i], unit.coords[i + 1] });
            }

            var geometry = new PolygonGeometry
            {
                Coordinates = new List<List<List<double>>> { ring }
            };

            features.Add(new GeoJsonFeature<SoilProperties>
            {
                Properties = new SoilProperties
                {
                    MuKey = $"sample_{unit.musym}",
                    MuSym = unit.musym,
                    MuName = unit.muname,
                    SoilClayPct = unit.clay,
                    SoilKsatUmPerS = unit.ksat,
                    Note = "Synthetic test data"
                },
                Geometry = geometry
            });
        }
        
        return features;
    }
}
