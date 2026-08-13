using System.Globalization;
using System.Text.Json;
using NetTopologySuite.IO;
using SoilDataProcessor;
using MapSandBox.Shared.Models;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;

class Program
{
    private static readonly HttpClient _httpClient = new();
    private const string SSURGO_API_BASE = "https://SDMDataAccess.sc.egov.usda.gov/Tabular/post.rest";
    
    static async Task Main(string[] args)
    {
        Console.WriteLine("SSURGO Soil Data Processor - Parker County Full Import");
        Console.WriteLine("======================================================");

        try
        {
            bool useFullCounty = args.Contains("--full-county");
            bool uploadOnly = args.Contains("--upload");
            bool allowSampleData = args.Contains("--allow-sample-data");

            if (uploadOnly)
            {
                Console.WriteLine("SSURGO Soil Data Uploader");
                Console.WriteLine("=========================");
                await UploadSoilDataToAzure();
                return;
            }

            string areaGeometry;
            if (useFullCounty)
            {
                Console.WriteLine("1. Loading Parker County boundary...");
                areaGeometry = LoadParkerCountyBoundary();
                Console.WriteLine("   ✅ Parker County boundary loaded successfully");
            }
            else
            {
                Console.WriteLine("1. Using small test area in Parker County...");
                Console.WriteLine("   (Use --full-county flag for complete county processing)");

                // Create small test area in Parker County (approximately 1 km²)
                areaGeometry = "POLYGON((-97.800 32.750, -97.790 32.750, -97.790 32.760, -97.800 32.760, -97.800 32.750))";
            }

            List<GeoJsonFeature<SoilProperties>> geoJsonFeatures;

            if (useFullCounty)
            {
                Console.WriteLine("2. Processing Parker County in chunks...");
                Console.WriteLine("   (Required due to 32MB API response limit)");

                // Process county in chunks to stay under API limits
                geoJsonFeatures = await ProcessCountyInChunks(areaGeometry);
            }
            else
            {
                Console.WriteLine("2. Querying real SSURGO API for test area...");
                Console.WriteLine("   (Querying actual USDA data)");

                // Query real SSURGO data for small test area
                var ssurgoResponse = await QuerySSURGOApiAsync(areaGeometry);

                // Debug: Show what we got from the API
                Console.WriteLine($"   API Response: {ssurgoResponse.Substring(0, Math.Min(200, ssurgoResponse.Length))}...");

                var rawFeatures = await ConvertSSURGOToGeoJSONWithGeometry(ssurgoResponse);

                // Always trim to Parker County boundary for clean output
                Console.WriteLine("   Trimming test area to Parker County boundary...");
                var parkerCountyWkt = LoadParkerCountyBoundary();
                geoJsonFeatures = TrimToCountyBoundary(rawFeatures, parkerCountyWkt);
                Console.WriteLine($"   After boundary trimming: {geoJsonFeatures.Count} features");
            }

            // Fallback to sample data only when explicitly requested
            if (geoJsonFeatures.Count == 0)
            {
                if (allowSampleData)
                {
                    Console.WriteLine("   No data returned from API, falling back to sample data (--allow-sample-data)...");
                    geoJsonFeatures = CreateSampleSoilData();
                }
                else
                {
                    Console.WriteLine("ERROR: No soil data returned from the SSURGO API.");
                    Console.WriteLine("       Re-run with --allow-sample-data to write synthetic sample data instead.");
                    Environment.ExitCode = 1;
                    return;
                }
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
            
            // Write files with standard Parker County names
            await File.WriteAllTextAsync(Path.Combine(outputDir, "parker-county-combined.geojson"), combinedGeoJson);
            await File.WriteAllTextAsync(Path.Combine(outputDir, "parker-county-clay.geojson"), clayVisualizationGeoJson);
            await File.WriteAllTextAsync(Path.Combine(outputDir, "parker-county-ksat.geojson"), ksatVisualizationGeoJson);

            Console.WriteLine("✅ Successfully generated soil data files:");
            Console.WriteLine($"   - {Path.Combine(outputDir, "parker-county-combined.geojson")}");
            Console.WriteLine($"   - {Path.Combine(outputDir, "parker-county-clay.geojson")}");
            Console.WriteLine($"   - {Path.Combine(outputDir, "parker-county-ksat.geojson")}");
            
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
            Environment.ExitCode = 1;
        }
    }
    
    private static async Task<string> QuerySSURGOApiAsync(string areaGeometry, int maxRetries = 3)
    {
        var query = BuildVectorSoilDataQuery(areaGeometry);

        var requestBody = new { query = query, format = "json" };
        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                if (attempt == 1)
                {
                    Console.WriteLine("     Querying SSURGO API...");
                }
                else
                {
                    Console.WriteLine($"     Retry attempt {attempt}/{maxRetries}...");
                }

                var response = await _httpClient.PostAsync(SSURGO_API_BASE, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();

                    // Check for size limit error
                    if (errorContent.Contains("exceeds") || errorContent.Contains("limit") ||
                        errorContent.Contains("32") || errorContent.Contains("too large"))
                    {
                        throw new InvalidOperationException($"Query exceeds API size limits: {errorContent}");
                    }

                    Console.WriteLine($"     API Error ({response.StatusCode}): {errorContent}");

                    if (attempt < maxRetries && (response.StatusCode == System.Net.HttpStatusCode.InternalServerError ||
                                                response.StatusCode == System.Net.HttpStatusCode.BadGateway ||
                                                response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable))
                    {
                        await Task.Delay(1000 * attempt); // Exponential backoff
                        continue;
                    }

                    throw new HttpRequestException($"SSURGO API returned {response.StatusCode}: {errorContent}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();

                // Monitor response size
                var sizeMB = responseContent.Length / (1024.0 * 1024.0);
                if (sizeMB > 30)
                {
                    Console.WriteLine($"     WARNING: Response {sizeMB:F1}MB approaching 32MB limit");
                }
                else if (sizeMB > 20)
                {
                    Console.WriteLine($"     Response size: {sizeMB:F1}MB (safe)");
                }
                else if (sizeMB > 1)
                {
                    Console.WriteLine($"     Response size: {sizeMB:F1}MB");
                }
                else
                {
                    Console.WriteLine($"     Response size: {responseContent.Length:N0} characters");
                }

                return responseContent;
            }
            catch (HttpRequestException ex) when (attempt < maxRetries)
            {
                Console.WriteLine($"     Network error on attempt {attempt}: {ex.Message}");
                await Task.Delay(1000 * attempt); // Exponential backoff + respectful delay
            }
            catch (TaskCanceledException ex) when (attempt < maxRetries)
            {
                Console.WriteLine($"     Request timeout on attempt {attempt}: {ex.Message}");
                await Task.Delay(2000 * attempt); // Longer delay for timeouts
            }
        }

        throw new Exception($"Failed to query SSURGO API after {maxRetries} attempts");
    }

    private static async Task<string> GetSoilGeometriesAsync(List<string> mukeys, int maxRetries = 3)
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

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                if (attempt == 1)
                {
                    Console.WriteLine($"     Querying geometry API for {mukeys.Count} map units...");
                }
                else
                {
                    Console.WriteLine($"     Geometry API retry {attempt}/{maxRetries}...");
                }

                var response = await _httpClient.PostAsync(SSURGO_API_BASE, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();

                    // Check for size limit error
                    if (errorContent.Contains("exceeds") || errorContent.Contains("limit") ||
                        errorContent.Contains("32") || errorContent.Contains("too large"))
                    {
                        throw new InvalidOperationException($"Geometry query exceeds API size limits: {errorContent}");
                    }

                    Console.WriteLine($"     Geometry API Error ({response.StatusCode}): {errorContent}");

                    if (attempt < maxRetries && (response.StatusCode == System.Net.HttpStatusCode.InternalServerError ||
                                                response.StatusCode == System.Net.HttpStatusCode.BadGateway ||
                                                response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable))
                    {
                        await Task.Delay(1000 * attempt);
                        continue;
                    }

                    throw new HttpRequestException($"SSURGO Geometry API returned {response.StatusCode}: {errorContent}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();

                // Monitor response size
                var sizeMB = responseContent.Length / (1024.0 * 1024.0);
                if (sizeMB > 30)
                {
                    Console.WriteLine($"     WARNING: Geometry response {sizeMB:F1}MB approaching 32MB limit");
                }
                else if (sizeMB > 1)
                {
                    Console.WriteLine($"     Geometry response: {sizeMB:F1}MB");
                }
                else
                {
                    Console.WriteLine($"     Geometry response: {responseContent.Length:N0} characters");
                }

                return responseContent;
            }
            catch (HttpRequestException ex) when (attempt < maxRetries)
            {
                Console.WriteLine($"     Geometry API network error on attempt {attempt}: {ex.Message}");
                await Task.Delay(1000 * attempt);
            }
            catch (TaskCanceledException ex) when (attempt < maxRetries)
            {
                Console.WriteLine($"     Geometry API timeout on attempt {attempt}: {ex.Message}");
                await Task.Delay(2000 * attempt);
            }
        }

        throw new Exception($"Failed to query SSURGO Geometry API after {maxRetries} attempts");
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

    private static SoilProperties CloneProperties(SoilProperties original)
    {
        return new SoilProperties
        {
            MuKey = original.MuKey,
            MuSym = original.MuSym,
            MuName = original.MuName,
            SoilClayPct = original.SoilClayPct,
            SoilKsatUmPerS = original.SoilKsatUmPerS,
            Note = original.Note,
            PolygonCount = original.PolygonCount
        };
    }

    private static async Task<List<GeoJsonFeature<SoilProperties>>> ProcessCountyInChunks(string countyBoundaryWkt)
    {
        Console.WriteLine("   Getting all map units in Parker County...");
        var allMukeys = await GetAllMapUnitsInCounty(countyBoundaryWkt);
        Console.WriteLine($"   Found {allMukeys.Count} unique map units in Parker County");

        // Create chunks based on map units (target ~50 mukeys per chunk for manageable API responses)
        var mukeyChunks = CreateMapUnitChunks(allMukeys, maxMukeysPerChunk: 50);
        var allFeatures = new List<GeoJsonFeature<SoilProperties>>();

        Console.WriteLine($"   Created {mukeyChunks.Count} map unit chunks (max 50 mukeys each)");

        for (int i = 0; i < mukeyChunks.Count; i++)
        {
            var mukeyChunk = mukeyChunks[i];
            Console.WriteLine($"   Processing chunk {i + 1}/{mukeyChunks.Count} ({mukeyChunk.Count} map units)...");

            try
            {
                // Get soil properties for this chunk of map units
                var chunkResponse = await QuerySSURGOApiForMapUnits(mukeyChunk);

                // Check response size before processing
                var responseSizeMB = chunkResponse.Length / (1024.0 * 1024.0);
                Console.WriteLine($"     Properties response: {responseSizeMB:F1}MB");

                var chunkFeatures = await ConvertSSURGOToGeoJSONWithGeometry(chunkResponse);
                allFeatures.AddRange(chunkFeatures);

                Console.WriteLine($"     Added {chunkFeatures.Count} features from chunk {i + 1}");

                // Conservative delay to be respectful to USDA servers
                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"     ERROR: Failed to process chunk {i + 1}: {ex.Message}");
                Console.WriteLine($"     Continuing with remaining chunks...");
            }
        }

        Console.WriteLine($"   Total features collected: {allFeatures.Count}");
        var dedupedFeatures = DeduplicateFeatures(allFeatures);

        Console.WriteLine("   Trimming to Parker County boundary...");
        var trimmedFeatures = TrimToCountyBoundary(dedupedFeatures, countyBoundaryWkt);
        Console.WriteLine($"   After boundary trimming: {trimmedFeatures.Count} features");

        return trimmedFeatures;
    }

    private static async Task<List<string>> GetAllMapUnitsInCounty(string countyBoundaryWkt)
    {
        var query = $@"
            SELECT DISTINCT mukey
            FROM SDA_Get_Mukey_from_intersection_with_WktWgs84('{countyBoundaryWkt}')";

        var requestBody = new { query = query, format = "json" };
        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        Console.WriteLine("     Querying for Parker County map unit list...");

        var response = await _httpClient.PostAsync(SSURGO_API_BASE, content);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Failed to get Parker County map units: {response.StatusCode}: {errorContent}");
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        var apiResponse = JsonSerializer.Deserialize<SsurgoApiResponse>(responseContent);

        if (apiResponse?.Table == null)
        {
            return new List<string>();
        }

        // Extract mukeys from the response
        var mukeys = apiResponse.Table
            .Where(row => row.Length > 0)
            .Select(row => row[0])
            .Where(mukey => !string.IsNullOrEmpty(mukey))
            .Distinct()
            .ToList();

        Console.WriteLine($"     Found {mukeys.Count} unique map units");
        return mukeys;
    }

    private static List<List<string>> CreateMapUnitChunks(List<string> allMukeys, int maxMukeysPerChunk)
    {
        var chunks = new List<List<string>>();
        for (int i = 0; i < allMukeys.Count; i += maxMukeysPerChunk)
        {
            var chunk = allMukeys.Skip(i).Take(maxMukeysPerChunk).ToList();
            chunks.Add(chunk);
        }
        return chunks;
    }

    private static async Task<string> QuerySSURGOApiForMapUnits(List<string> mukeys)
    {
        var mukeyList = string.Join("','", mukeys);
        var query = $@"
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
            WHERE mu.mukey IN ('{mukeyList}')
            AND co.comppct_r >= 25
            AND ch.hzdept_r = 0
            ORDER BY mu.mukey, co.comppct_r DESC";

        var requestBody = new { query = query, format = "json" };
        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        Console.WriteLine($"     Querying soil properties for {mukeys.Count} map units...");

        var response = await _httpClient.PostAsync(SSURGO_API_BASE, content);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"SSURGO API returned {response.StatusCode}: {errorContent}");
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"     Properties response: {responseContent.Length:N0} characters");

        return responseContent;
    }

    private static List<string> CreateSpatialChunks(string countyBoundaryWkt, double maxChunkSize)
    {
        // Parse the WKT to get bounds
        var wktReader = new WKTReader();
        var countyGeometry = wktReader.Read(countyBoundaryWkt);
        var envelope = countyGeometry.EnvelopeInternal;

        // Calculate grid dimensions
        var width = envelope.Width;
        var height = envelope.Height;
        var countyAreaDegrees = width * height;

        // Estimate grid size (rough approximation)
        var chunksNeeded = Math.Ceiling(900.0 / maxChunkSize); // 900 km² total area
        var gridSize = Math.Ceiling(Math.Sqrt(chunksNeeded));

        var cellWidth = width / gridSize;
        var cellHeight = height / gridSize;

        Console.WriteLine($"   County bounds: {width:F4}° × {height:F4}° (estimated 900 km²)");
        Console.WriteLine($"   Grid: {gridSize}×{gridSize} = {gridSize * gridSize} cells ({cellWidth:F4}° × {cellHeight:F4}° each)");

        var chunks = new List<string>();

        for (int row = 0; row < gridSize; row++)
        {
            for (int col = 0; col < gridSize; col++)
            {
                var minX = envelope.MinX + col * cellWidth;
                var maxX = envelope.MinX + (col + 1) * cellWidth;
                var minY = envelope.MinY + row * cellHeight;
                var maxY = envelope.MinY + (row + 1) * cellHeight;

                // Create a polygon for this grid cell
                var cellWkt = $"POLYGON(({minX} {minY}, {maxX} {minY}, {maxX} {maxY}, {minX} {maxY}, {minX} {minY}))";

                // Check if this cell intersects with the county boundary
                var cellGeometry = wktReader.Read(cellWkt);
                if (countyGeometry.Intersects(cellGeometry))
                {
                    // Get the intersection to avoid processing areas outside the county
                    var intersection = countyGeometry.Intersection(cellGeometry);
                    var wktWriter = new WKTWriter();
                    chunks.Add(wktWriter.Write(intersection));
                }
            }
        }

        return chunks;
    }

    private static List<GeoJsonFeature<SoilProperties>> DeduplicateFeatures(List<GeoJsonFeature<SoilProperties>> features)
    {
        // Remove duplicate features based on MuKey and geometry
        var seen = new HashSet<string>();
        var deduplicated = new List<GeoJsonFeature<SoilProperties>>();

        foreach (var feature in features)
        {
            // Create a unique key based on mukey and simplified geometry
            var geometryKey = JsonSerializer.Serialize(feature.Geometry);
            var key = $"{feature.Properties.MuKey}:{geometryKey.GetHashCode()}";

            if (!seen.Contains(key))
            {
                seen.Add(key);
                deduplicated.Add(feature);
            }
        }

        Console.WriteLine($"   Deduplication: {features.Count} → {deduplicated.Count} features");
        return deduplicated;
    }

    private static List<GeoJsonFeature<SoilProperties>> TrimToCountyBoundary(List<GeoJsonFeature<SoilProperties>> features, string countyBoundaryWkt)
    {
        var wktReader = new WKTReader();
        var wktWriter = new WKTWriter();
        var countyGeometry = wktReader.Read(countyBoundaryWkt);

        var trimmedFeatures = new List<GeoJsonFeature<SoilProperties>>();
        int trimmedCount = 0;
        int removedCount = 0;

        foreach (var feature in features)
        {
            try
            {
                // Convert the soil feature geometry to NTS geometry
                var geoJsonString = JsonSerializer.Serialize(feature.Geometry);
                var geoJsonReader = new GeoJsonReader();
                var soilGeometry = geoJsonReader.Read<NetTopologySuite.Geometries.Geometry>(geoJsonString);

                // Check if the soil geometry intersects with Parker County
                if (countyGeometry.Intersects(soilGeometry))
                {
                    // Get only the part that's within Parker County
                    var intersection = countyGeometry.Intersection(soilGeometry);

                    // Only keep if the intersection has actual area
                    if (!intersection.IsEmpty && intersection.Area > 0)
                    {
                        // Convert the trimmed geometry back to GeoJSON
                        var trimmedWkt = wktWriter.Write(intersection);
                        var trimmedGeoJsonGeometry = ConvertWktToGeoJsonGeometry(trimmedWkt);

                        // Create new feature with trimmed geometry
                        var trimmedFeature = new GeoJsonFeature<SoilProperties>
                        {
                            Properties = CloneProperties(feature.Properties),
                            Geometry = trimmedGeoJsonGeometry
                        };

                        // Update the note to indicate trimming
                        trimmedFeature.Properties.Note = feature.Properties.Note + " (trimmed to Parker County)";

                        trimmedFeatures.Add(trimmedFeature);
                        trimmedCount++;
                    }
                    else
                    {
                        removedCount++;
                    }
                }
                else
                {
                    removedCount++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"     Warning: Failed to trim feature for mukey {feature.Properties.MuKey}: {ex.Message}");
                // Keep the original feature if trimming fails
                trimmedFeatures.Add(feature);
            }
        }

        Console.WriteLine($"     Trimmed: {trimmedCount} features, Removed: {removedCount} features (outside county)");
        return trimmedFeatures;
    }

    private static string LoadParkerCountyBoundary()
    {
        var boundaryPath = Path.Combine("..", "CrisDataProcessor", "Data", "parker-county-boundary.geojson");

        if (!File.Exists(boundaryPath))
        {
            throw new FileNotFoundException($"Parker County boundary file not found at: {boundaryPath}");
        }

        var boundaryGeoJson = File.ReadAllText(boundaryPath);
        var featureCollection = JsonSerializer.Deserialize<GeoJsonFeatureCollection<Dictionary<string, object>>>(boundaryGeoJson);

        if (featureCollection?.Features == null || featureCollection.Features.Count == 0)
        {
            throw new InvalidDataException("Parker County boundary GeoJSON contains no features");
        }

        // Convert the county boundary geometry to WKT for SSURGO API
        var countyGeometry = featureCollection.Features.First().Geometry;
        if (countyGeometry == null)
        {
            throw new InvalidDataException("Parker County boundary feature has no geometry");
        }
        return ConvertGeometryToWkt(countyGeometry);
    }

    private static string ConvertGeometryToWkt(GeoJsonGeometry geometry)
    {
        try
        {
            // Use NetTopologySuite for robust GeoJSON to WKT conversion
            var geoJsonString = JsonSerializer.Serialize(geometry);
            var geoJsonReader = new GeoJsonReader();
            var ntsGeometry = geoJsonReader.Read<NetTopologySuite.Geometries.Geometry>(geoJsonString);

            var wktWriter = new WKTWriter();
            return wktWriter.Write(ntsGeometry);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to convert geometry to WKT: {ex.Message}", ex);
        }
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

                // Create separate features for each polygon with complete coverage
                var allFeatures = new List<GeoJsonFeature<SoilProperties>>();
                foreach (var propertiesFeature in propertiesFeatures)
                {
                    var mukey = propertiesFeature.Properties.MuKey;
                    if (geometryGroups.TryGetValue(mukey, out var wktGeometries) && wktGeometries.Count > 0)
                    {
                        // Create a separate feature for each polygon with this soil type
                        for (int i = 0; i < wktGeometries.Count; i++)
                        {
                            try
                            {
                                var newFeature = new GeoJsonFeature<SoilProperties>
                                {
                                    Properties = CloneProperties(propertiesFeature.Properties),
                                    Geometry = ConvertWktToGeoJsonGeometry(wktGeometries[i])
                                };
                                newFeature.Properties.Note = $"Real SSURGO data - polygon {i + 1}/{wktGeometries.Count}";
                                newFeature.Properties.PolygonCount = wktGeometries.Count;
                                allFeatures.Add(newFeature);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"   Warning: Failed to convert polygon {i + 1} for mukey {mukey}: {ex.Message}");
                            }
                        }
                        Console.WriteLine($"   Created {wktGeometries.Count} features for map unit {mukey}");
                    }
                    else
                    {
                        // No geometry found — skip the unit rather than publish the placeholder rectangle
                        Console.WriteLine($"   Warning: No geometry returned for map unit {mukey}; skipping unit");
                    }
                }

                Console.WriteLine($"   Total features created: {allFeatures.Count} (from {propertiesFeatures.Count} map units)");
                return allFeatures;
            }
            else
            {
                Console.WriteLine($"   Warning: No geometry data returned; skipping {propertiesFeatures.Count} map units (placeholder geometry is not published)");
                return new List<GeoJsonFeature<SoilProperties>>();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   Warning: Failed to retrieve geometries: {ex.Message}");
            Console.WriteLine($"   Skipping {propertiesFeatures.Count} map units (placeholder geometry is not published)");
            return new List<GeoJsonFeature<SoilProperties>>();
        }
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
                ComponentPct = double.TryParse(row[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct) ? (double?)pct : null,
                ClayPct = double.TryParse(row[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var clay) ? (double?)clay : null,
                Ksat = double.TryParse(row[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var ksat) ? (double?)ksat : null
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

    private static async Task UploadSoilDataToAzure()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString =
            Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING")
            ?? config.GetConnectionString("AzureStorage")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__AzureStorage");

        var containerName = config["ContainerName"] ?? "$web";
        var soilDataDir = config["SoilDataDirectory"] ?? "../MapSandBox/wwwroot/soil-data";

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine("ERROR: Azure Storage connection string not found.\n"
                + "Set AZURE_STORAGE_CONNECTION_STRING in your .env or environment, "
                + "or provide ConnectionStrings:AzureStorage in appsettings.json.");
            return;
        }

        Console.WriteLine($"Using soil data directory: {soilDataDir}");

        var uploader = new SoilDataUploader(connectionString, containerName);
        await uploader.UploadSoilDataAsync(soilDataDir);
    }
}

public class SoilDataUploader
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly BlobContainerClient _containerClient;

    public SoilDataUploader(string connectionString, string containerName)
    {
        _blobServiceClient = new BlobServiceClient(connectionString);
        _containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
    }

    public async Task UploadSoilDataAsync(string soilDataDirectory)
    {
        if (!Directory.Exists(soilDataDirectory))
        {
            Console.WriteLine($"ERROR: Soil data directory not found: {soilDataDirectory}");
            return;
        }

        // Look for Parker County soil data files
        var soilFiles = Directory.GetFiles(soilDataDirectory, "parker-county-*.geojson");
        Console.WriteLine($"Found {soilFiles.Length} soil data files to upload");

        if (soilFiles.Length == 0)
        {
            Console.WriteLine("No Parker County soil data files found to upload.");
            Console.WriteLine("Expected files: parker-county-clay.geojson, parker-county-ksat.geojson, parker-county-combined.geojson");
            return;
        }

        var startTime = DateTime.Now;
        var uploaded = 0;

        foreach (var filePath in soilFiles)
        {
            try
            {
                await UploadSoilFileAsync(filePath, soilDataDirectory);
                uploaded++;
                Console.WriteLine($"Uploaded: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to upload {Path.GetFileName(filePath)}: {ex.Message}");
            }
        }

        var totalTime = DateTime.Now - startTime;
        Console.WriteLine($"Upload completed: {uploaded}/{soilFiles.Length} files uploaded in {totalTime.TotalSeconds:F1} seconds");
    }

    private async Task UploadSoilFileAsync(string filePath, string soilDataDirectory)
    {
        // Create blob path for soil data
        var fileName = Path.GetFileName(filePath);
        var blobName = $"soil-data/{fileName}";

        var blobClient = _containerClient.GetBlobClient(blobName);

        // Set cache headers appropriate for geospatial data
        var headers = new Azure.Storage.Blobs.Models.BlobHttpHeaders
        {
            CacheControl = "public, max-age=86400", // 24 hours (soil data updates infrequently)
            ContentType = "application/geo+json"
        };

        // Overwrite existing blob and apply headers
        Console.WriteLine($"Uploading {fileName} ({new FileInfo(filePath).Length / (1024 * 1024):F1} MB)...");
        await blobClient.UploadAsync(filePath, overwrite: true);
        await blobClient.SetHttpHeadersAsync(headers);
    }
}
