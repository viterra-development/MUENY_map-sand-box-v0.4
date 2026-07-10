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
            // NOTE: TWI, Slope, SCA, SPI, Elevation, Soil Clay, Soil Ksat layers were removed
            // because their tile/geojson data lived only on Azure and the Azure subscription is
            // inactive. Restore by rehosting tiles on Cloudflare R2 and re-adding LayerConfig
            // entries that point at the new URLs.
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
                    ["sizeMinPixels"] = 16,
                    ["sizeMaxPixels"] = 32
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
            },
            new LayerConfig
            {
                Id = "wp-parcels-trips",
                Type = "GeoJson",
                DataUrl = "/willow-park-parcels-with-trips.geojson",
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["filled"] = true,
                    ["stroked"] = true,
                    ["getLineColor"] = new int[] { 50, 50, 50, 200 },
                    ["getLineWidth"] = 1,
                    ["opacity"] = 0.75,
                    ["pickable"] = true,
                    ["autoHighlight"] = true
                }
            },
            new LayerConfig
            {
                Id = "aledo-parcels-trips",
                Type = "GeoJson",
                DataUrl = "/aledo-parcels-with-trips.geojson",
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["filled"] = true,
                    ["stroked"] = true,
                    ["getLineColor"] = new int[] { 50, 50, 50, 200 },
                    ["getLineWidth"] = 1,
                    ["opacity"] = 0.75,
                    ["pickable"] = true,
                    ["autoHighlight"] = true
                }
            },
            new LayerConfig
            {
                Id = "azle-parcels-trips",
                Type = "GeoJson",
                DataUrl = "/azle-parcels-with-trips.geojson",
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["filled"] = true,
                    ["stroked"] = true,
                    ["getLineColor"] = new int[] { 50, 50, 50, 200 },
                    ["getLineWidth"] = 1,
                    ["opacity"] = 0.75,
                    ["pickable"] = true,
                    ["autoHighlight"] = true
                }
            },
            new LayerConfig
            {
                Id = "hudson-oaks-parcels-trips",
                Type = "GeoJson",
                DataUrl = "/hudson-oaks-parcels-with-trips.geojson",
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["filled"] = true,
                    ["stroked"] = true,
                    ["getLineColor"] = new int[] { 50, 50, 50, 200 },
                    ["getLineWidth"] = 1,
                    ["opacity"] = 0.75,
                    ["pickable"] = true,
                    ["autoHighlight"] = true
                }
            },
            new LayerConfig
            {
                Id = "annetta-parcels-trips",
                Type = "GeoJson",
                DataUrl = "/annetta-parcels-with-trips.geojson",
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["filled"] = true,
                    ["stroked"] = true,
                    ["getLineColor"] = new int[] { 50, 50, 50, 200 },
                    ["getLineWidth"] = 1,
                    ["opacity"] = 0.75,
                    ["pickable"] = true,
                    ["autoHighlight"] = true
                }
            },
            new LayerConfig
            {
                Id = "annetta-north-parcels-trips",
                Type = "GeoJson",
                DataUrl = "/annetta-north-parcels-with-trips.geojson",
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["filled"] = true,
                    ["stroked"] = true,
                    ["getLineColor"] = new int[] { 50, 50, 50, 200 },
                    ["getLineWidth"] = 1,
                    ["opacity"] = 0.75,
                    ["pickable"] = true,
                    ["autoHighlight"] = true
                }
            },
            new LayerConfig
            {
                Id = "annetta-south-parcels-trips",
                Type = "GeoJson",
                DataUrl = "/annetta-south-parcels-with-trips.geojson",
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["filled"] = true,
                    ["stroked"] = true,
                    ["getLineColor"] = new int[] { 50, 50, 50, 200 },
                    ["getLineWidth"] = 1,
                    ["opacity"] = 0.75,
                    ["pickable"] = true,
                    ["autoHighlight"] = true
                }
            },
            new LayerConfig
            {
                Id = "mineral-wells-parcels-trips",
                Type = "GeoJson",
                DataUrl = "/mineral-wells-parcels-with-trips.geojson",
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["filled"] = true,
                    ["stroked"] = true,
                    ["getLineColor"] = new int[] { 50, 50, 50, 200 },
                    ["getLineWidth"] = 1,
                    ["opacity"] = 0.75,
                    ["pickable"] = true,
                    ["autoHighlight"] = true
                }
            },
            new LayerConfig
            {
                Id = "reno-parcels-trips",
                Type = "GeoJson",
                DataUrl = "/reno-parcels-with-trips.geojson",
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["filled"] = true,
                    ["stroked"] = true,
                    ["getLineColor"] = new int[] { 50, 50, 50, 200 },
                    ["getLineWidth"] = 1,
                    ["opacity"] = 0.75,
                    ["pickable"] = true,
                    ["autoHighlight"] = true
                }
            },
            new LayerConfig
            {
                Id = "springtown-parcels-trips",
                Type = "GeoJson",
                DataUrl = "/springtown-parcels-with-trips.geojson",
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["filled"] = true,
                    ["stroked"] = true,
                    ["getLineColor"] = new int[] { 50, 50, 50, 200 },
                    ["getLineWidth"] = 1,
                    ["opacity"] = 0.75,
                    ["pickable"] = true,
                    ["autoHighlight"] = true
                }
            },
            new LayerConfig
            {
                Id = "weatherford-parcels-trips",
                Type = "GeoJson",
                DataUrl = "/weatherford-parcels-with-trips.geojson",
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["filled"] = true,
                    ["stroked"] = true,
                    ["getLineColor"] = new int[] { 50, 50, 50, 200 },
                    ["getLineWidth"] = 1,
                    ["opacity"] = 0.75,
                    ["pickable"] = true,
                    ["autoHighlight"] = true
                }
            },
            new LayerConfig
            {
                Id = "cris-road-stress",
                Type = "GeoJson",
                DataUrl = "/cris-data/parker-county-road-stress-map.geojson",
                Visible = false,
                Properties = new Dictionary<string, object>
                {
                    ["filled"] = false,
                    ["stroked"] = true,
                    ["opacity"] = 0.85,
                    ["pickable"] = true,
                    ["autoHighlight"] = true,
                    ["onClick"] = "handleRoadStressClick"
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
            new LayerInfo { Id = "cris-crashes", Name = "CRIS Crash Points", Visible = true },
            new LayerInfo { Id = "cris-risk-segments", Name = "CRIS Risk Segments", Visible = true },
            new LayerInfo { Id = "cris-intersections", Name = "⚠️ CRIS Intersection Risks", Visible = false },
            new LayerInfo { Id = "txdot-city-boundaries", Name = "City Boundaries (TxDOT)", Visible = false },
            new LayerInfo { Id = "wp-parcels-trips", Name = "Trip Generation (Willow Park)", Visible = false },
            new LayerInfo { Id = "aledo-parcels-trips", Name = "Trip Generation (Aledo)", Visible = false },
            new LayerInfo { Id = "azle-parcels-trips", Name = "Trip Generation (Azle)", Visible = false },
            new LayerInfo { Id = "mineral-wells-parcels-trips", Name = "Trip Generation (Mineral Wells)", Visible = false },
            new LayerInfo { Id = "reno-parcels-trips", Name = "Trip Generation (Reno)", Visible = false },
            new LayerInfo { Id = "springtown-parcels-trips", Name = "Trip Generation (Springtown)", Visible = false },
            new LayerInfo { Id = "weatherford-parcels-trips", Name = "Trip Generation (Weatherford)", Visible = false },
            new LayerInfo { Id = "hudson-oaks-parcels-trips", Name = "Trip Generation (Hudson Oaks)", Visible = false },
            new LayerInfo { Id = "annetta-parcels-trips", Name = "Trip Generation (Annetta)", Visible = false },
            new LayerInfo { Id = "annetta-north-parcels-trips", Name = "Trip Generation (Annetta North)", Visible = false },
            new LayerInfo { Id = "annetta-south-parcels-trips", Name = "Trip Generation (Annetta South)", Visible = false },
            new LayerInfo { Id = "cris-road-stress", Name = "Road Stress Index", Visible = false }
            // new LayerInfo { Id = "noaa-rainfall-parker-points", Name = "NOAA Rainfall Points", Visible = false },
            // new LayerInfo { Id = "noaa-rainfall-parker-heatmap", Name = "NOAA Rainfall Heatmap", Visible = false }
        };
    }
}