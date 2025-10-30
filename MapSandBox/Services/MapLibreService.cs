using MapSandBox.Models;

namespace MapSandBox.Services;

public class MapLibreService
{
    private readonly List<BaseMapStyle> _availableStyles;
    private readonly AzureTileConfig _azureTileConfig;
    private readonly SoilDataConfig _soilDataConfig;

    public MapLibreService(AzureTileConfig azureTileConfig, SoilDataConfig soilDataConfig)
    {
        _azureTileConfig = azureTileConfig;
        _soilDataConfig = soilDataConfig;
    
        // Initialize available styles
        _availableStyles = new List<BaseMapStyle>
        {
            new BaseMapStyle
            {
                Id = "light",
                Name = "Light",
                Url = "https://basemaps.cartocdn.com/gl/positron-gl-style/style.json",
                Description = "Light theme with minimal colors"
            },
            new BaseMapStyle
            {
                Id = "dark",
                Name = "Dark",
                Url = "https://basemaps.cartocdn.com/gl/dark-matter-gl-style/style.json",
                Description = "Dark theme for low-light environments"
            },
            new BaseMapStyle
            {
                Id = "voyager",
                Name = "Voyager",
                Url = "https://basemaps.cartocdn.com/gl/voyager-gl-style/style.json",
                Description = "Balanced style with good contrast"
            },
            new BaseMapStyle
            {
                Id = "osm",
                Name = "OpenStreetMap",
                Url = "https://tiles.openfreemap.org/styles/liberty",
                Description = "OpenStreetMap style"
            }
        };
    }
    
    public MapLibreConfig GetDefaultConfig()
    {
        return new MapLibreConfig
        {
            Latitude = 32.758,  // Parker County, TX center latitude
            Longitude = -97.65, // Parker County, TX center longitude
            Zoom = 14,          // Zoom level appropriate for city view
            Bearing = 0,
            Pitch = 0,
            BaseMap = GetDefaultBaseMap(),
            Layers = GetDefaultLayers()
        };
    }
    
    public BaseMapConfig GetDefaultBaseMap()
    {
        var defaultStyle = _availableStyles.First(s => s.Id == GetDefaultStyleId());
        return new BaseMapConfig
        {
            Style = defaultStyle.Url,
            ShowControls = true,
            ShowAttribution = true,
            Name = defaultStyle.Name
        };
    }
    
    public string GetDefaultStyleId()
    {
        return "voyager";
    }
    
    private string GetTileBaseUrl()
    {

        Console.WriteLine($"UseCdn: {_azureTileConfig.UseCdn}");
        Console.WriteLine($"CdnUrl: {_azureTileConfig.CdnUrl}");
        Console.WriteLine($"BaseUrl: {_azureTileConfig.BaseUrl}");

        return _azureTileConfig.UseCdn ? _azureTileConfig.CdnUrl : _azureTileConfig.BaseUrl;
    }
    
    private string GetTileUrl(string layerType)
    {
        var baseUrl = GetTileBaseUrl();
        if (string.IsNullOrEmpty(baseUrl))
        {
            // Fallback to local tiles if no Azure configuration
            return $"/tiles/{layerType}/{"{z}"}/{"{x}"}/{"{y}"}.png";
        }
        return $"{baseUrl}/tiles/{layerType}/{"{z}"}/{"{x}"}/{"{y}"}.png";
    }
    
    private string GetSoilDataUrl(string fileName)
    {
        return _soilDataConfig.GetSoilDataUrl(fileName);
    }
    
    public List<BaseMapStyle> GetAvailableBaseMapStyles()
    {
        return _availableStyles;
    }
    
    public List<LayerConfig> GetDefaultLayers()
    {
        return new List<LayerConfig>
        {
            new LayerConfig
            {
                Id = "parker-roads-base",
                Type = "GeoJson",
                DataUrl = "/parker-county-roads.geojson",
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["stroked"] = true,
                    ["filled"] = false,
                    ["getLineColor"] = new int[] { 120, 120, 120, 128 }, // Gray for base roads
                    ["getLineWidth"] = 1,
                    ["lineWidthMinPixels"] = 1,
                    ["opacity"] = 0.6,
                    ["pickable"] = true,
                    ["onClick"] = "handleRoadClick"
                }
            },
            new LayerConfig
            {
                Id = "parker-roads-traffic",
                Type = "Path",
                DataUrl = "/parker-roads-with-traffic.geojson",
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["getPath"] = "getCoordinates",
                    ["getColor"] = "getTrafficGradientColor",
                    ["getWidth"] = "getTrafficWidth",
                    ["widthMinPixels"] = 1,
                    ["widthMaxPixels"] = 6,
                    ["capRounded"] = true,
                    ["jointRounded"] = true,
                    ["opacity"] = 0.9,
                    ["pickable"] = true,
                    ["autoHighlight"] = true,
                    ["onClick"] = "handleTrafficRoadClick"
                }
            },
            new LayerConfig
            {
                Id = "parker-roads-traffic-phase1",
                Type = "Path",
                DataUrl = "/parker-roads-with-traffic-phase1.geojson",
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["getPath"] = "getCoordinates",
                    ["getColor"] = "getTrafficGradientColor",
                    ["getWidth"] = "getTrafficWidth",
                    ["widthMinPixels"] = 1,
                    ["widthMaxPixels"] = 6,
                    ["capRounded"] = true,
                    ["jointRounded"] = true,
                    ["opacity"] = 0.9,
                    ["pickable"] = true,
                    ["autoHighlight"] = true,
                    ["onClick"] = "handleTrafficRoadClick"
                }
            },
            new LayerConfig
            {
                Id = "county-cad-parcels",
                Type = "GeoJson",
                DataUrl = "/sample-data/county-cad-parcel-test.geojson",
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["filled"] = true,
                    ["stroked"] = true,
                    ["getFillColor"] = new int[] { 255, 0, 0, 120 }, // bright red fill, semi-transparent
                    ["getLineColor"] = new int[] { 255, 0, 0, 255 }, // bright red outline
                    ["pickable"] = true,
                    ["autoHighlight"] = true
                }
            },
            new LayerConfig
            {
                Id = "traffic-counts",
                Type = "TileLayer", // Changed to TileLayer type
                DataUrl = "/tiles/traffic-counts/{z}/{x}/{y}.geojson", // Tile URL template
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["filled"] = true,
                    ["stroked"] = true,
                    ["pointRadiusMinPixels"] = 3,
                    ["pointRadiusMaxPixels"] = 50,
                    ["getRadius"] = "getTrafficRadius",
                    ["getFillColor"] = "getTrafficColor",
                    ["getLineColor"] = new int[] { 0, 0, 0, 255 }, // black outline
                    ["getLineWidth"] = 2,
                    ["pickable"] = true,
                    ["autoHighlight"] = true,
                    ["onClick"] = "handleTrafficCountClick"
                }
            },
            // Raster TWI tiles generated from DEM workflow (XYZ) - now served from Azure
            new LayerConfig
            {
                Id = "parker-twi",
                Type = "RasterTile",
                DataUrl = GetTileUrl("parker-twi"),
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["opacity"] = 0.75,
                    ["minZoom"] = 8,
                    ["maxZoom"] = 18,
                    ["tileSize"] = 256
                }
            },
            // Slope layer (degrees)
            new LayerConfig
            {
                Id = "parker-slope",
                Type = "RasterTile", 
                DataUrl = GetTileUrl("parker-slope"),
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["opacity"] = 0.7,
                    ["minZoom"] = 8,
                    ["maxZoom"] = 18,
                    ["tileSize"] = 256
                }
            },
            // Specific Catchment Area layer
            new LayerConfig  
            {
                Id = "parker-sca",
                Type = "RasterTile",
                DataUrl = GetTileUrl("parker-sca"), 
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["opacity"] = 0.7,
                    ["minZoom"] = 8,
                    ["maxZoom"] = 18,
                    ["tileSize"] = 256
                }
            },
            // Stream Power Index layer
            new LayerConfig
            {
                Id = "parker-spi", 
                Type = "RasterTile",
                DataUrl = GetTileUrl("parker-spi"),
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["opacity"] = 0.7,
                    ["minZoom"] = 8,
                    ["maxZoom"] = 18, 
                    ["tileSize"] = 256
                }
            },
            // Base elevation layer
            new LayerConfig
            {
                Id = "parker-elevation",
                Type = "RasterTile",
                DataUrl = GetTileUrl("parker-elevation"),
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["opacity"] = 0.8,
                    ["minZoom"] = 8,
                    ["maxZoom"] = 18,
                    ["tileSize"] = 256
                }
            },
            
            // SSURGO Soil Map Units - Vector-based from Azure Blob Storage (Industry Standard)
            new LayerConfig
            {
                Id = "soil-clay-visualization",
                Type = "GeoJson",
                DataUrl = GetSoilDataUrl("parker-county-clay.geojson"),
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["filled"] = true,
                    ["stroked"] = true,
                    ["getFillColor"] = "getSoilClayColor", // JavaScript function for clay % coloring
                    ["getLineColor"] = new int[] { 139, 69, 19, 255 }, // Brown soil boundary
                    ["getLineWidth"] = 2,
                    ["opacity"] = 0.8,
                    ["pickable"] = true,
                    ["autoHighlight"] = true,
                    ["onClick"] = "handleSoilUnitClick"
                }
            },
            
            // Soil permeability visualization layer
            new LayerConfig
            {
                Id = "soil-ksat-visualization",
                Type = "GeoJson",
                DataUrl = GetSoilDataUrl("parker-county-ksat.geojson"),
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["filled"] = true,
                    ["stroked"] = true,
                    ["getFillColor"] = "getSoilKsatColor", // JavaScript function for Ksat coloring
                    ["getLineColor"] = new int[] { 139, 69, 19, 255 },
                    ["getLineWidth"] = 1,
                    ["opacity"] = 0.7,
                    ["pickable"] = true,
                    ["autoHighlight"] = true,
                    ["onClick"] = "handleSoilUnitClick"
                }
            },
            new LayerConfig
            {
                Id = "cris-risk-segments",
                Type = "PathLayer",
                DataUrl = "/cris-data/parker-county-risk-segments-traffic-deckgl.json",
                Visible = true,
                Properties = new Dictionary<string, object>
                {
                    ["widthMinPixels"] = 1,
                    ["widthMaxPixels"] = 8
                }
            },
            new LayerConfig
            {
                Id = "cris-crashes",
                Type = "ScatterplotLayer",
                DataUrl = "/cris-data/parker-county-crashes-clustered-deckgl.json",
                Visible = true,
                Properties = new Dictionary<string, object>
                {
                    ["radiusMinPixels"] = 4,
                    ["radiusMaxPixels"] = 25,
                    ["stroked"] = false,
                    ["pickable"] = true,
                    ["autoHighlight"] = true
                }
            },
            new LayerConfig
            {
                Id = "cris-intersections",
                Type = "ScatterplotLayer",
                DataUrl = "/cris-data/parker-county-intersection-risks-deckgl.json",
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["radiusMinPixels"] = 6,
                    ["radiusMaxPixels"] = 25,
                    ["stroked"] = true,
                    ["getLineColor"] = "[0, 0, 0, 255]",
                    ["lineWidthMinPixels"] = 1
                }
            },
            new LayerConfig
            {
                Id = "txdot-city-boundaries",
                Type = "GeoJson",
                DataUrl = "/txdot-city-boundaries.geojson",
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["filled"] = true,
                    ["stroked"] = true,
                    ["getFillColor"] = new int[] { 100, 150, 200, 30 },  // Light blue, semi-transparent
                    ["getLineColor"] = new int[] { 0, 100, 200, 255 },   // Darker blue border
                    ["getLineWidth"] = 2,
                    ["lineWidthMinPixels"] = 1,
                    ["lineWidthMaxPixels"] = 3,
                    ["opacity"] = 0.6,
                    ["pickable"] = true,
                    ["autoHighlight"] = true,
                    ["onClick"] = "handleCityBoundaryClick"
                }
            }
            // Commented out until scaling issues are resolved
            // new LayerConfig
            // {
            //     Id = "noaa-rainfall-parker-points",
            //     Type = "ScatterplotLayer",
            //     DataUrl = "/noaa-rainfall-parker-county.geojson",
            //     Visible = false,
            //     Properties = new Dictionary<string, object>
            //     {
            //         ["getPosition"] = "getCoordinates",
            //         ["getRadius"] = "getRainfallRadius",
            //         ["getFillColor"] = "getRainfallColor",
            //         ["radiusMinPixels"] = 2,
            //         ["radiusMaxPixels"] = 8,
            //         ["pickable"] = true,
            //         ["stroked"] = true,
            //         ["getLineColor"] = "[0, 0, 0, 128]",
            //         ["lineWidthMinPixels"] = 1
            //     }
            // },
            // new LayerConfig
            // {
            //     Id = "noaa-rainfall-parker-heatmap",
            //     Type = "HeatmapLayer",
            //     DataUrl = "/noaa-rainfall-parker-county.geojson",
            //     Visible = false,
            //     Properties = new Dictionary<string, object>
            //     {
            //         ["getPosition"] = "getCoordinates",
            //         ["getWeight"] = "getRainfallWeight",
            //         ["radiusPixels"] = 60,
            //         ["intensity"] = 2,
            //         ["threshold"] = 0.01,
            //         ["colorRange"] = "getRainfallColorRange"
            //     }
            // }
        };
    }
    
    public List<LayerInfo> GetLayerInfo()
    {
        return new List<LayerInfo>
        {
            new LayerInfo { Id = "parker-roads-base", Name = "Parker County Roads (Base)", Visible = false },
            new LayerInfo { Id = "parker-roads-traffic", Name = "Parker County Roads (Traffic)", Visible = false },
            new LayerInfo { Id = "parker-roads-traffic-phase1", Name = "Traffic - Phase 1 (Interpolation IDW)", Visible = false },
            new LayerInfo { Id = "county-cad-parcels", Name = "County CAD Parcels", Visible = false },
            new LayerInfo { Id = "traffic-counts", Name = "Traffic Count Locations", Visible = false },
            new LayerInfo { Id = "parker-twi", Name = "Topographic Wetness Index (TWI)", Visible = false },
            new LayerInfo { Id = "parker-slope", Name = "Slope (degrees)", Visible = false },
            new LayerInfo { Id = "parker-sca", Name = "Specific Catchment Area", Visible = false },
            new LayerInfo { Id = "parker-spi", Name = "Stream Power Index", Visible = false },
            new LayerInfo { Id = "parker-elevation", Name = "Base Elevation", Visible = false },
            new LayerInfo { Id = "soil-clay-visualization", Name = "Soil Clay Content (%)", Visible = false },
            new LayerInfo { Id = "soil-ksat-visualization", Name = "Soil Permeability (Ksat)", Visible = false },
            new LayerInfo { Id = "cris-crashes", Name = "CRIS Crash Points", Visible = true },
            new LayerInfo { Id = "cris-risk-segments", Name = "CRIS Risk Segments", Visible = true },
            new LayerInfo { Id = "cris-intersections", Name = "CRIS Intersection Risks", Visible = false },
            new LayerInfo { Id = "txdot-city-boundaries", Name = "City Boundaries (TxDOT)", Visible = false }
            // new LayerInfo { Id = "noaa-rainfall-parker-points", Name = "NOAA Rainfall Points", Visible = false },
            // new LayerInfo { Id = "noaa-rainfall-parker-heatmap", Name = "NOAA Rainfall Heatmap", Visible = false }
        };
    }
}