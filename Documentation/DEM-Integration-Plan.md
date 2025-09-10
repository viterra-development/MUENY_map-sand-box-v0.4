# DEM Integration Plan for MapSandBox

## Current Status

✅ **Completed:**
- DEM data downloaded and processed (in `/DEM/` directory)
- Elevation derivatives generated: TWI, slope, SCA, SPI
- MapLibre raster tile layer support implemented
- TWI layer configuration added to MapLibreService

❌ **Missing:**
- Web tiles generation from GeoTIFF outputs
- Additional elevation layer configurations
- Tile serving setup

## Available Processed Data

Located in `/DEM/`:
- `parker_1m_dem_cog_twi.tif` - Topographic Wetness Index
- `parker_1m_dem_cog_slope_deg.tif` - Slope (degrees)  
- `parker_1m_dem_cog_sca.tif` - Specific Catchment Area
- `parker_1m_dem_cog_spi.tif` - Stream Power Index
- `parker_1m_dem_cog_filled.tif` - Filled DEM (base elevation)

## Implementation Plan

### Step 1: Generate Web Tiles

Convert GeoTIFFs to XYZ tiles for web display:

```bash
# Create tiles directory
mkdir -p MapSandBox/wwwroot/tiles

# Generate tiles for each elevation product (zoom levels 8-18)
cd DEM

# TWI tiles (already configured in code)
gdal2tiles.py -z 8-18 -w leaflet parker_1m_dem_cog_twi.tif ../MapSandBox/wwwroot/tiles/parker-twi

# Slope tiles
gdal2tiles.py -z 8-18 -w leaflet parker_1m_dem_cog_slope_deg.tif ../MapSandBox/wwwroot/tiles/parker-slope

# SCA tiles  
gdal2tiles.py -z 8-18 -w leaflet parker_1m_dem_cog_sca.tif ../MapSandBox/wwwroot/tiles/parker-sca

# SPI tiles
gdal2tiles.py -z 8-18 -w leaflet parker_1m_dem_cog_spi.tif ../MapSandBox/wwwroot/tiles/parker-spi

# Base elevation tiles
gdal2tiles.py -z 8-18 -w leaflet parker_1m_dem_cog_filled.tif ../MapSandBox/wwwroot/tiles/parker-elevation
```

### Step 2: Add Layer Configurations

Update `MapLibreService.cs` to include all elevation layers:

```csharp
// Add to GetDefaultLayers() method after existing parker-twi layer:

// Slope layer
new LayerConfig
{
    Id = "parker-slope",
    Type = "RasterTile", 
    DataUrl = "/tiles/parker-slope/{z}/{x}/{y}.png",
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
    DataUrl = "/tiles/parker-sca/{z}/{x}/{y}.png", 
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
    DataUrl = "/tiles/parker-spi/{z}/{x}/{y}.png",
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
    DataUrl = "/tiles/parker-elevation/{z}/{x}/{y}.png",
    Visible = false,
    Properties = new Dictionary<string, object>
    {
        ["opacity"] = 0.8,
        ["minZoom"] = 8,
        ["maxZoom"] = 18,
        ["tileSize"] = 256
    }
}
```

### Step 3: Update Layer Info

Add to `GetLayerInfo()` method:

```csharp
new LayerInfo { Id = "parker-slope", Name = "Slope (degrees)", Visible = false },
new LayerInfo { Id = "parker-sca", Name = "Specific Catchment Area", Visible = false },
new LayerInfo { Id = "parker-spi", Name = "Stream Power Index", Visible = false },
new LayerInfo { Id = "parker-elevation", Name = "Base Elevation", Visible = false }
```

### Step 4: Verify Tile Generation

Check that tiles are generated correctly:

```bash
# Verify tile structure
ls -la MapSandBox/wwwroot/tiles/parker-twi/
ls -la MapSandBox/wwwroot/tiles/parker-twi/14/  # Should show x directories
ls -la MapSandBox/wwwroot/tiles/parker-twi/14/3400/  # Should show .png files
```

### Step 5: Test Integration

1. Run the application: `dotnet run --project MapSandBox`
2. Navigate to MapLibre page
3. Toggle elevation layers in layer control
4. Verify layers display over Parker County area
5. Check browser network tab for successful tile requests

## Expected Results

After implementation:
- 5 new elevation layers available in MapLibre interface
- Layers display hydrological and topographic analysis
- TWI shows wet/dry areas (higher values = wetter)
- Slope shows terrain steepness
- SCA shows water flow accumulation
- SPI shows erosion potential
- Base elevation shows terrain height

## File Structure After Completion

```
MapSandBox/wwwroot/tiles/
├── parker-twi/
│   ├── 8/...
│   ├── 9/...
│   └── 18/...
├── parker-slope/
├── parker-sca/
├── parker-spi/
└── parker-elevation/
```

## Notes

- Tile generation may take several minutes per layer
- Total tile storage will be ~500MB-2GB depending on data complexity
- Tiles are static - regenerate if source GeoTIFFs change
- Consider adding color ramps/legends for better data visualization
- Current zoom levels (8-18) provide county to parcel-level detail