using System.Text.Json;
using NetTopologySuite.IO;
using SoilDataProcessor;

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
            Console.WriteLine("1. Creating sample soil data for testing...");
            Console.WriteLine("   (Using synthetic data for demonstration purposes)");
            
            // For testing, create sample soil map units with representative data
            var geoJsonFeatures = CreateSampleSoilData();
            
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
                Console.WriteLine($"   Map Unit: {props["musym"]} - {props["muname"]}");
                Console.WriteLine($"   Clay: {props["soil_clay_pct"]}%, Ksat: {props["soil_ksat_um_per_s"]} μm/s");
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
    
    private static List<SoilGeoJsonFeature> ConvertSSURGOToGeoJSON(string ssurgoResponse)
    {
        var ssurgoData = JsonSerializer.Deserialize<SsurgoApiResponse>(ssurgoResponse);
        if (ssurgoData?.Table == null)
        {
            return new List<SoilGeoJsonFeature>();
        }
        
        var features = new List<SoilGeoJsonFeature>();
        
        // Group by map unit key and aggregate component data
        var groupedData = ssurgoData.Table
            .GroupBy(r => r.MuKey)
            .Select(g => new
            {
                MuKey = g.Key,
                MuSym = g.First().MuSym,
                MuName = g.First().MuName,
                Geom = g.First().Geom,
                // Weight-averaged soil properties by component percentage
                ClayPct = g.Where(r => r.SoilClayPct.HasValue && r.ComponentPct.HasValue).Any() ?
                          g.Where(r => r.SoilClayPct.HasValue && r.ComponentPct.HasValue)
                          .Sum(r => r.SoilClayPct!.Value * r.ComponentPct!.Value) /
                          g.Where(r => r.SoilClayPct.HasValue && r.ComponentPct.HasValue)
                          .Sum(r => r.ComponentPct!.Value) : 0,
                Ksat = g.Where(r => r.SoilKsatUmPerS.HasValue && r.ComponentPct.HasValue).Any() ?
                      g.Where(r => r.SoilKsatUmPerS.HasValue && r.ComponentPct.HasValue)
                      .Sum(r => r.SoilKsatUmPerS!.Value * r.ComponentPct!.Value) /
                      g.Where(r => r.SoilKsatUmPerS.HasValue && r.ComponentPct.HasValue)
                      .Sum(r => r.ComponentPct!.Value) : 0
            })
            .Where(g => !string.IsNullOrEmpty(g.Geom))
            .ToList();
        
        Console.WriteLine($"   Processing {groupedData.Count} unique map units...");
        
        foreach (var mapUnit in groupedData)
        {
            try
            {
                // Convert WKT geometry to GeoJSON geometry using NetTopologySuite
                var geoJsonGeometry = ConvertWktToGeoJsonGeometry(mapUnit.Geom);
                
                features.Add(new SoilGeoJsonFeature
                {
                    Type = "Feature",
                    Properties = new Dictionary<string, object>
                    {
                        ["mukey"] = mapUnit.MuKey,
                        ["musym"] = mapUnit.MuSym,
                        ["muname"] = mapUnit.MuName ?? "",
                        ["soil_clay_pct"] = Math.Round(mapUnit.ClayPct, 1),
                        ["soil_ksat_um_per_s"] = Math.Round(mapUnit.Ksat, 3)
                    },
                    Geometry = geoJsonGeometry
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   Warning: Failed to convert map unit {mapUnit.MuKey}: {ex.Message}");
            }
        }
        
        return features;
    }
    
    private static object ConvertWktToGeoJsonGeometry(string wkt)
    {
        // Use NetTopologySuite for robust WKT to GeoJSON conversion
        var wktReader = new WKTReader();
        var geometry = wktReader.Read(wkt);
        
        // Convert to GeoJSON format
        var geoJsonWriter = new GeoJsonWriter();
        var geoJsonString = geoJsonWriter.Write(geometry);
        
        // Parse the geometry portion of the GeoJSON
        var geoJsonObject = JsonSerializer.Deserialize<JsonElement>(geoJsonString);
        return geoJsonObject.GetProperty("geometry").Deserialize<object>()!;
    }
    
    private static string CreateGeoJsonCollection(List<SoilGeoJsonFeature> features)
    {
        var collection = new SoilGeoJsonCollection { Features = features };
        return JsonSerializer.Serialize(collection, new JsonSerializerOptions { WriteIndented = false });
    }
    
    private static string CreateClayVisualizationGeoJson(List<SoilGeoJsonFeature> features)
    {
        // Create version optimized for clay percentage visualization
        var clayFeatures = features.Select(f => new SoilGeoJsonFeature
        {
            Type = f.Type,
            Geometry = f.Geometry,
            Properties = new Dictionary<string, object>
            {
                ["mukey"] = f.Properties["mukey"],
                ["musym"] = f.Properties["musym"],
                ["muname"] = f.Properties["muname"],
                ["soil_clay_pct"] = f.Properties["soil_clay_pct"],
                ["visualization"] = "clay"
            }
        }).ToList();
        
        return CreateGeoJsonCollection(clayFeatures);
    }
    
    private static string CreateKsatVisualizationGeoJson(List<SoilGeoJsonFeature> features)
    {
        // Create version optimized for permeability visualization
        var ksatFeatures = features.Select(f => new SoilGeoJsonFeature
        {
            Type = f.Type,
            Geometry = f.Geometry,
            Properties = new Dictionary<string, object>
            {
                ["mukey"] = f.Properties["mukey"],
                ["musym"] = f.Properties["musym"],
                ["muname"] = f.Properties["muname"],
                ["soil_ksat_um_per_s"] = f.Properties["soil_ksat_um_per_s"],
                ["visualization"] = "ksat"
            }
        }).ToList();
        
        return CreateGeoJsonCollection(ksatFeatures);
    }
    
    private static List<SoilGeoJsonFeature> CreateSampleSoilData()
    {
        // Create representative soil map units for Parker County, TX area
        // Using realistic soil types and properties found in North Texas
        var features = new List<SoilGeoJsonFeature>();
        
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
            // Create polygon geometry
            var coordinates = new List<List<double[]>>();
            var ring = new List<double[]>();
            
            for (int i = 0; i < unit.coords.Length; i += 2)
            {
                ring.Add(new double[] { unit.coords[i], unit.coords[i + 1] });
            }
            coordinates.Add(ring);
            
            var geometry = new
            {
                type = "Polygon",
                coordinates = coordinates
            };
            
            features.Add(new SoilGeoJsonFeature
            {
                Type = "Feature",
                Properties = new Dictionary<string, object>
                {
                    ["mukey"] = $"sample_{unit.musym}",
                    ["musym"] = unit.musym,
                    ["muname"] = unit.muname,
                    ["soil_clay_pct"] = unit.clay,
                    ["soil_ksat_um_per_s"] = unit.ksat
                },
                Geometry = geometry
            });
        }
        
        return features;
    }
}
