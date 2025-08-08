using MapSandBox.Models;

namespace MapSandBox.Services;

public class MapLibreService
{
    private readonly List<BaseMapStyle> _availableStyles;
    
    public MapLibreService()
    {
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
                Visible = true,
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
                Visible = true,
                Properties = new Dictionary<string, object>
                {
                    ["getPath"] = "getCoordinates",
                    ["getColor"] = "getTrafficGradientColor",
                    ["getWidth"] = "getTrafficWidth",
                    ["widthMinPixels"] = 2,
                    ["widthMaxPixels"] = 12,
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
                Visible = true,
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
                Visible = true,
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
            }
        };
    }
    
    public List<LayerInfo> GetLayerInfo()
    {
        return new List<LayerInfo>
        {
            new LayerInfo { Id = "parker-roads-base", Name = "Parker County Roads (Base)", Visible = true },
            new LayerInfo { Id = "parker-roads-traffic", Name = "Parker County Roads (Traffic)", Visible = true },
            new LayerInfo { Id = "county-cad-parcels", Name = "County CAD Parcels", Visible = true },
            new LayerInfo { Id = "traffic-counts", Name = "Traffic Count Locations", Visible = true }
        };
    }
}