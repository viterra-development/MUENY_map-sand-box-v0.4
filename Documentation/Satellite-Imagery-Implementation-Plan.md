# Satellite Imagery Implementation Plan

**Date:** January 2025
**Project:** MapSandBox Blazor WebAssembly Application
**Focus Area:** Parker County, Texas
**Objective:** Add high-resolution satellite imagery layers to deck.gl and MapLibre mapping backends

---

## Executive Summary

### Recommended Implementation Strategy

**Phase 1 (Immediate):** Start with NASA GIBS + MapTiler Satellite for quick, production-ready satellite layers
**Phase 2 (2-4 weeks):** Add NAIP high-resolution imagery specifically for Parker County, Texas
**Phase 3 (Optional):** Self-host TiTiler for custom COG processing if advanced requirements emerge

### Why This Approach?

1. **Quick Time-to-Value:** NASA GIBS provides immediate, free satellite imagery with zero setup
2. **Progressive Enhancement:** Build from simple (API-based) to advanced (self-hosted) as needs grow
3. **Cost-Effective:** Free tier usage for development, paid options only if needed
4. **Leverages Existing Skills:** NAIP tiling builds on your hydro/elevation tiling experience
5. **Texas-Optimized:** NAIP provides 0.6m resolution perfect for Parker County use cases

---

## Detailed Source Comparison

### Option Matrix

| Source | Resolution | Coverage | Latency | Cost | Complexity | Best For |
|--------|-----------|----------|---------|------|-----------|----------|
| **NASA GIBS** | 250m-500m | Global | 3-5 hrs | Free | Low | Recent/current conditions, testing |
| **MapTiler Satellite** | 30cm-1m | Global | Varies | Freemium | Low | Production base maps |
| **NAIP (AWS)** | 0.6m (60cm) | US only | 2-3 years | Free* | Medium | High-res US imagery |
| **Sentinel-2 (COG)** | 10m | Global | 5 days | Free | Medium | Multi-spectral analysis |
| **TiTiler (Self-host)** | Varies | Custom | Custom | Infrastructure | High | Custom processing pipelines |
| **Maxar/Commercial** | 30-50cm | Global | Current | $$$ | Low | Premium applications |

*NAIP is free data but may incur AWS egress costs outside us-east-1

---

## Phase 1: Quick Win with Tile Services

### 1A. NASA GIBS Integration (Free, Immediate)

**Advantages:**
- Zero cost, no API key required
- Near real-time imagery (3-5 hour latency)
- 1000+ visualizations available
- Excellent for weather, fires, floods, current conditions
- WMTS/WMS support

**Implementation:**

#### Available GIBS Products for Your Use Case:
- **MODIS Terra/Aqua True Color:** Daily global imagery at 250m
- **VIIRS True Color:** Daily at 500m resolution
- **Landsat 8 True Color:** 15m resolution (updates every 16 days)

#### Tile URL Template:
```
https://gibs.earthdata.nasa.gov/wmts/epsg3857/best/{layer}/default/{time}/{tilematrixset}/{z}/{y}/{x}.{format}
```

**Example for VIIRS True Color (Best for recent imagery):**
```
https://gibs.earthdata.nasa.gov/wmts/epsg3857/best/VIIRS_NOAA20_CorrectedReflectance_TrueColor/default/{TIME}/GoogleMapsCompatible_Level9/{z}/{y}/{x}.jpg
```

Where `{TIME}` is in format `YYYY-MM-DD` (e.g., `2025-01-24`)

#### C# Model Extension:
```csharp
// Add to MapLibreModels.cs
public class SatelliteLayerConfig
{
    public string Id { get; set; } = "satellite-layer";
    public SatelliteSource Source { get; set; } = SatelliteSource.NasaGibs;
    public string? Date { get; set; } // For GIBS time-based imagery
    public double Opacity { get; set; } = 1.0;
    public int MinZoom { get; set; } = 0;
    public int MaxZoom { get; set; } = 9; // GIBS max zoom
}

public enum SatelliteSource
{
    NasaGibs,
    MapTiler,
    Naip,
    Sentinel2
}
```

#### MapLibre Integration:
```javascript
// In maplibre-deckgl-integration.js
export function addSatelliteLayer(mapInstance, config) {
    const today = new Date().toISOString().split('T')[0];
    const date = config.date || today;

    mapInstance.addSource('satellite-gibs', {
        type: 'raster',
        tiles: [
            `https://gibs.earthdata.nasa.gov/wmts/epsg3857/best/VIIRS_NOAA20_CorrectedReflectance_TrueColor/default/${date}/GoogleMapsCompatible_Level9/{z}/{y}/{x}.jpg`
        ],
        tileSize: 256,
        minzoom: 0,
        maxzoom: 9
    });

    mapInstance.addLayer({
        id: config.id,
        type: 'raster',
        source: 'satellite-gibs',
        paint: {
            'raster-opacity': config.opacity
        }
    }, 'deck-overlay'); // Insert below deck.gl overlay
}
```

#### deck.gl Pure Integration:
```javascript
// In map.js - for pure deck.gl implementation
import { TileLayer, BitmapLayer } from 'deck.gl';

function createGIBSSatelliteLayer(config) {
    const today = new Date().toISOString().split('T')[0];
    const date = config.date || today;

    return new TileLayer({
        id: config.id,
        data: `https://gibs.earthdata.nasa.gov/wmts/epsg3857/best/VIIRS_NOAA20_CorrectedReflectance_TrueColor/default/${date}/GoogleMapsCompatible_Level9/{z}/{y}/{x}.jpg`,
        minZoom: 0,
        maxZoom: 9,
        tileSize: 256,
        opacity: config.opacity,
        renderSubLayers: props => {
            const { boundingBox } = props.tile;
            return new BitmapLayer(props, {
                data: null,
                image: props.data,
                bounds: [
                    boundingBox[0][0], boundingBox[0][1],
                    boundingBox[1][0], boundingBox[1][1]
                ]
            });
        }
    });
}
```

---

### 1B. MapTiler Satellite Integration (Freemium)

**Advantages:**
- High quality (30cm-1m resolution depending on area)
- Global coverage with consistent quality
- Free tier: 100,000 tile loads/month
- Seamless MapLibre integration
- Well-maintained, production-ready
- Same API key works for satellite and other map styles

**Free Tier Limitations:**
- 100,000 map views per month
- MapTiler watermark required
- Rate limiting applies

**Paid Tiers:**
- **Start:** €25/month - 200k views, no watermark
- **Standard:** €99/month - 1M views
- **Enterprise:** Custom pricing

**Implementation:**

#### Get API Key:
1. Sign up at https://www.maptiler.com/cloud/
2. Get free API key from dashboard
3. Store in user secrets or environment variable

#### C# Configuration:
```csharp
// Add to appsettings.json (or user secrets)
{
  "MapTiler": {
    "ApiKey": "YOUR_MAPTILER_API_KEY_HERE",
    "SatelliteStyleUrl": "https://api.maptiler.com/maps/satellite/style.json"
  }
}

// Add configuration class
public class MapTilerConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string SatelliteStyleUrl { get; set; } = string.Empty;
}
```

#### Service Implementation:
```csharp
// Services/SatelliteService.cs
public class SatelliteService
{
    private readonly MapTilerConfig _config;

    public SatelliteService(IConfiguration configuration)
    {
        _config = configuration.GetSection("MapTiler").Get<MapTilerConfig>()
            ?? new MapTilerConfig();
    }

    public string GetSatelliteTileUrl()
    {
        return $"https://api.maptiler.com/tiles/satellite-v2/{{z}}/{{x}}/{{y}}.jpg?key={_config.ApiKey}";
    }

    public string GetSatelliteStyleUrl()
    {
        return $"{_config.SatelliteStyleUrl}?key={_config.ApiKey}";
    }
}
```

#### MapLibre Integration (Recommended - Easiest):
```javascript
// Option A: Use complete satellite style
export function initializeMapWithSatellite(container, styleUrl) {
    return new maplibregl.Map({
        container: container,
        style: styleUrl, // MapTiler satellite style URL with API key
        center: [-97.65, 32.758], // Parker County
        zoom: 14,
        pitch: 0
    });
}

// Option B: Add as raster source to existing style
export function addMapTilerSatellite(mapInstance, apiKey, opacity) {
    mapInstance.addSource('maptiler-satellite', {
        type: 'raster',
        tiles: [
            `https://api.maptiler.com/tiles/satellite-v2/{z}/{x}/{y}.jpg?key=${apiKey}`
        ],
        tileSize: 512, // MapTiler uses 512px tiles
        minzoom: 0,
        maxzoom: 20
    });

    mapInstance.addLayer({
        id: 'maptiler-satellite-layer',
        type: 'raster',
        source: 'maptiler-satellite',
        paint: {
            'raster-opacity': opacity
        }
    });
}
```

#### deck.gl Integration:
```javascript
function createMapTilerSatelliteLayer(apiKey, opacity) {
    return new TileLayer({
        id: 'maptiler-satellite',
        data: `https://api.maptiler.com/tiles/satellite-v2/{z}/{x}/{y}.jpg?key=${apiKey}`,
        minZoom: 0,
        maxZoom: 20,
        tileSize: 512,
        opacity: opacity,
        renderSubLayers: props => {
            const { boundingBox } = props.tile;
            return new BitmapLayer(props, {
                data: null,
                image: props.data,
                bounds: [
                    boundingBox[0][0], boundingBox[0][1],
                    boundingBox[1][0], boundingBox[1][1]
                ]
            });
        }
    });
}
```

---

## Phase 2: NAIP High-Resolution for Parker County

### Why NAIP is Ideal

**Specific to Your Use Case:**
- Parker County, Texas is covered by NAIP
- 0.6m resolution (vs 10m Sentinel-2, 250m GIBS)
- Perfect for identifying buildings, roads, parcels
- Complements your existing Parker County parcel/road data
- Free data via AWS (requester-pays in us-east-1)

**Latest NAIP Cycles for Texas:**
- 2020, 2022 available now on AWS
- 2024 being processed (expected soon)
- Updates every 2 years for Texas

### NAIP Data Structure on AWS

**S3 Buckets:**
- `naip-visualization` - 3-band RGB COG (recommended for display)
- `naip-analytic` - 4-band RGB+NIR COG and MRF
- `naip-source` - Uncompressed source files (avoid - huge)

**Naming Convention:**
```
s3://naip-visualization/tx/2022/60cm/rgbir_cog/48367/
```
Where `48367` is Parker County FIPS code

**File Format:**
Cloud-Optimized GeoTIFF (COG) with internal tiling and overviews

### Implementation Approaches

#### Approach A: Direct COG Access (Simplest)

**Pros:**
- No infrastructure needed
- Minimal code
- Works with existing COG-capable tools

**Cons:**
- Must identify specific NAIP tiles that cover Parker County
- May need to mosaic multiple tiles
- AWS egress costs if serving to users

**Implementation:**
```javascript
// Using geotiff.js to read COG directly
import { fromUrl } from 'geotiff';

async function loadNAIPTile(s3Url) {
    const tiff = await fromUrl(s3Url);
    const image = await tiff.getImage();
    const data = await image.readRasters();
    // Render with deck.gl BitmapLayer
    return data;
}
```

#### Approach B: TiTiler Serverless (Recommended)

**Pros:**
- Dynamic tiling from COG
- No need to identify individual tiles
- Can mosaic multiple COGs automatically
- XYZ tile endpoint for easy integration

**Cons:**
- Requires deployment infrastructure
- Learning curve for TiTiler

**Implementation Steps:**

1. **Deploy TiTiler:**
   - Use AWS Lambda + API Gateway (CDK provided by TiTiler)
   - Or Docker container on cloud instance
   - Or local Docker for development

2. **TiTiler XYZ Endpoint:**
```
https://your-titiler.com/cog/tiles/{z}/{x}/{y}?url=s3://naip-visualization/tx/2022/60cm/rgbir_cog/48367/TILE_ID.tif
```

3. **Mosaic Multiple NAIP Tiles:**
```python
# Using cogeo-mosaic to create mosaic definition
from cogeo_mosaic.mosaic import MosaicJSON
from cogeo_mosaic.backends import MosaicBackend

# List all NAIP tiles for Parker County
naip_urls = [
    "s3://naip-visualization/tx/2022/60cm/rgbir_cog/48367/m_3009701_ne_14_060_20220703.tif",
    "s3://naip-visualization/tx/2022/60cm/rgbir_cog/48367/m_3009701_nw_14_060_20220703.tif",
    # ... more tiles
]

# Create mosaic
mosaic = MosaicJSON.from_urls(naip_urls)

# Save mosaic definition
with MosaicBackend("naip-parker-county.json", mosaic_def=mosaic) as mosaic:
    mosaic.write()
```

4. **Serve via TiTiler:**
```
https://your-titiler.com/mosaicjson/tiles/{z}/{x}/{y}?url=s3://your-bucket/naip-parker-county.json
```

5. **Integrate with MapLibre:**
```javascript
mapInstance.addSource('naip-satellite', {
    type: 'raster',
    tiles: [
        'https://your-titiler.com/mosaicjson/tiles/{z}/{x}/{y}?url=s3://your-bucket/naip-parker-county.json'
    ],
    tileSize: 256,
    minzoom: 10, // NAIP only useful at higher zooms
    maxzoom: 19
});

mapInstance.addLayer({
    id: 'naip-layer',
    type: 'raster',
    source: 'naip-satellite',
    paint: {
        'raster-opacity': 1.0
    }
});
```

#### Approach C: Pre-tile NAIP Locally (Your Expertise)

**Pros:**
- Full control over tiles
- Leverages your existing tiling experience
- No runtime COG processing
- Can optimize for your specific area of interest

**Cons:**
- Upfront processing time
- Storage requirements
- Manual updates when new NAIP data released

**Implementation:**

1. **Download NAIP tiles for Parker County:**
```bash
# Using AWS CLI
aws s3 sync s3://naip-visualization/tx/2022/60cm/rgbir_cog/48367/ ./naip-parker/ --request-payer requester
```

2. **Create mosaic VRT:**
```bash
gdalbuildvrt naip-parker-mosaic.vrt ./naip-parker/*.tif
```

3. **Generate XYZ tiles with GDAL:**
```bash
gdal2tiles.py -z 10-19 -w none --processes=4 naip-parker-mosaic.vrt ./naip-tiles/
```

4. **Serve tiles:**
   - Upload to CDN (CloudFront, Netlify, etc.)
   - Or serve from local wwwroot/tiles/naip/
   - Or use local tile server

5. **Integrate:**
```javascript
mapInstance.addSource('naip-local', {
    type: 'raster',
    tiles: [
        '/tiles/naip/{z}/{x}/{y}.png'
        // or 'https://your-cdn.com/naip/{z}/{x}/{y}.png'
    ],
    tileSize: 256,
    minzoom: 10,
    maxzoom: 19
});
```

**Recommended Tool Chain:**
- **GDAL** - For mosaic and tiling
- **cogeo-mosaic** - If using TiTiler
- **rasterio** - For COG manipulation
- **titiler** - For dynamic serving

---

## Phase 3: Self-Hosted TiTiler (Optional)

### When to Consider TiTiler

**Use Cases:**
- Need to serve multiple COG sources dynamically
- Want custom processing (band math, color correction)
- Require on-the-fly reprojection
- Need to update imagery sources without re-tiling

**Infrastructure Requirements:**

#### Option A: AWS Lambda (Serverless)
```bash
# Using TiTiler CDK
git clone https://github.com/developmentseed/titiler.git
cd deployment/aws
npm install
cdk deploy
```

**Costs:**
- Lambda requests: ~$0.20 per 1M requests
- API Gateway: ~$3.50 per 1M requests
- Data transfer: ~$0.09/GB

#### Option B: Docker Container
```bash
# Local development
docker run -p 8000:8000 ghcr.io/developmentseed/titiler:latest

# Production with docker-compose
services:
  titiler:
    image: ghcr.io/developmentseed/titiler:latest
    ports:
      - "8000:8000"
    environment:
      - CPL_VSIL_CURL_ALLOWED_EXTENSIONS=.tif,.tiff
      - GDAL_DISABLE_READDIR_ON_OPEN=EMPTY_DIR
```

### TiTiler Capabilities

**Endpoints:**
- `/cog/tiles/{z}/{x}/{y}` - Single COG
- `/mosaicjson/tiles/{z}/{x}/{y}` - Mosaic of COGs
- `/cog/info` - Metadata
- `/cog/statistics` - Band statistics
- `/cog/preview` - Quick preview image

**Example Usage:**
```javascript
// Band combination (e.g., false color from 4-band NAIP)
const url = 'https://titiler/cog/tiles/{z}/{x}/{y}?url=s3://naip/tile.tif&bidx=4,1,2&rescale=0,255'

// Custom color map
const url = 'https://titiler/cog/tiles/{z}/{x}/{y}?url=s3://naip/tile.tif&colormap_name=viridis'
```

---

## UI/UX Recommendations

### Satellite Layer Controls

#### Blazor Component (SatelliteControl.razor):
```razor
@inject SatelliteService SatelliteService
@inject IJSRuntime JSRuntime

<div class="satellite-control">
    <h3>Satellite Imagery</h3>

    <div class="source-selector">
        <label>Source:</label>
        <select @bind="SelectedSource" @bind:after="UpdateSource">
            <option value="none">None</option>
            <option value="gibs">NASA GIBS (Recent)</option>
            <option value="maptiler">MapTiler Satellite (High-Res)</option>
            <option value="naip">NAIP (Parker County)</option>
        </select>
    </div>

    @if (SelectedSource == "gibs")
    {
        <div class="date-selector">
            <label>Date:</label>
            <input type="date" @bind="SelectedDate" @bind:after="UpdateDate" max="@DateTime.Today.ToString("yyyy-MM-dd")" />
        </div>
    }

    <div class="opacity-control">
        <label>Opacity: @Opacity.ToString("P0")</label>
        <input type="range" min="0" max="1" step="0.1" @bind="Opacity" @bind:after="UpdateOpacity" />
    </div>
</div>

@code {
    private string SelectedSource = "none";
    private string SelectedDate = DateTime.Today.ToString("yyyy-MM-dd");
    private double Opacity = 1.0;

    private async Task UpdateSource()
    {
        await JSRuntime.InvokeVoidAsync("setSatelliteSource", SelectedSource, SelectedDate, Opacity);
    }

    private async Task UpdateDate()
    {
        await JSRuntime.InvokeVoidAsync("setSatelliteDate", SelectedDate);
    }

    private async Task UpdateOpacity()
    {
        await JSRuntime.InvokeVoidAsync("setSatelliteOpacity", Opacity);
    }
}
```

### Progressive Loading Strategy

**Zoom-Based Layer Switching:**
```javascript
// Show different sources at different zoom levels
function updateSatelliteLayerByZoom(map) {
    const zoom = map.getZoom();

    if (zoom < 10) {
        // Show GIBS or MapTiler at low zooms
        setLayerVisible('gibs-satellite', true);
        setLayerVisible('naip-satellite', false);
    } else {
        // Switch to NAIP at high zooms (if available)
        setLayerVisible('gibs-satellite', false);
        setLayerVisible('naip-satellite', true);
    }
}
```

---

## Cost Analysis

### Development/Testing Phase (Free Tier)

| Source | Monthly Cost | Limitations |
|--------|-------------|-------------|
| NASA GIBS | $0 | None - completely free |
| MapTiler | $0 | 100k tile loads, watermark required |
| NAIP (AWS) | $0* | Requester-pays for data transfer |
| TiTiler (local) | $0 | Development only |

*Minimal costs if staying in us-east-1 region

### Production Estimates (10,000 users, 100k monthly sessions)

**Scenario A: MapTiler Only**
- Cost: €99/month (Standard tier, 1M views)
- Simplest, no infrastructure

**Scenario B: NAIP via CDN**
- Processing: One-time setup
- Storage: ~50GB tiles = ~$1.15/month (S3)
- CDN: 500GB transfer = ~$40-80/month (CloudFront)
- Total: ~$50-85/month

**Scenario C: TiTiler on AWS Lambda**
- Lambda: 500k requests = ~$0.10
- API Gateway: 500k requests = ~$1.75
- Data transfer: 100GB = ~$9
- Total: ~$11/month + infrastructure

---

## Technical Considerations

### Performance

**Tile Loading Optimization:**
```javascript
// Prefetch tiles around viewport
mapInstance.on('moveend', () => {
    const bounds = mapInstance.getBounds();
    prefetchSurroundingTiles(bounds, currentZoom + 1);
});

// Cancel pending tile requests on zoom/pan
let currentRequests = [];
mapInstance.on('movestart', () => {
    currentRequests.forEach(req => req.abort());
    currentRequests = [];
});
```

**Caching Strategy:**
- Browser: Set appropriate cache headers on tiles
- CDN: Enable CloudFront or similar for tile caching
- ServiceWorker: Consider offline tile caching

### COG Best Practices

**When Working with NAIP COGs:**
1. Always use internal tiling and overviews
2. Read only required zoom levels (use `GetOverview()`)
3. Use byte range requests (automatic with COG)
4. Consider JPEG compression for RGB data
5. Use LZW or DEFLATE for lossless needs

**Validation:**
```bash
# Verify COG structure
rio cogeo validate naip-tile.tif

# Create COG from non-COG
rio cogeo create input.tif output_cog.tif --overview-level 6
```

### Integration with Existing Layers

**Layer Ordering:**
```javascript
// Recommended layer stack (bottom to top)
1. Base map (MapLibre style)
2. Satellite imagery (GIBS/MapTiler/NAIP)
3. Parcel boundaries (your existing county-cad-parcel-test.geojson)
4. Roads (your existing parker-county-roads.geojson)
5. deck.gl overlays (airports, rivers, etc.)
6. Labels/annotations
```

**Blend Modes:**
```javascript
// Satellite as underlay with semi-transparent parcels
mapInstance.setPaintProperty('satellite-layer', 'raster-opacity', 0.7);
mapInstance.setPaintProperty('parcels', 'fill-opacity', 0.5);
```

---

## Migration Path from Phase to Phase

### Phase 1 → Phase 2

**Adding NAIP without breaking GIBS/MapTiler:**
```javascript
// Add zoom-based layer switching
const satelliteLayers = {
    low: 'gibs-satellite',      // z0-9
    medium: 'maptiler-satellite', // z10-13
    high: 'naip-satellite'        // z14+
};

mapInstance.on('zoom', () => {
    const zoom = mapInstance.getZoom();
    updateActiveSatelliteLayer(zoom);
});
```

### Phase 2 → Phase 3

**Migrating from Static Tiles to TiTiler:**
1. Deploy TiTiler
2. Test with same NAIP data
3. Update tile URLs in configuration
4. A/B test performance
5. Decommission static tiles after validation

---

## Sample Implementation Timeline

### Week 1: Phase 1 Implementation
- **Day 1-2:** Add NASA GIBS to MapLibre
  - Create SatelliteService
  - Implement JavaScript integration
  - Add date selector UI
- **Day 3-4:** Add MapTiler integration
  - Set up API key configuration
  - Implement layer switching
  - Add opacity controls
- **Day 5:** Testing and refinement
  - Browser compatibility
  - Performance testing
  - UI/UX polish

### Week 2-3: NAIP Research
- **Week 2:**
  - Identify Parker County NAIP tiles
  - Download sample tiles for testing
  - Experiment with COG readers
  - Decide on tiling vs TiTiler approach
- **Week 3:**
  - Implement chosen NAIP approach
  - Create Parker County mosaic
  - Integrate with existing UI
  - Performance optimization

### Week 4+: Optional TiTiler
- Deploy TiTiler to cloud platform
- Migrate NAIP to dynamic serving
- Implement band combinations
- Add temporal comparison features

---

## Success Metrics

### Phase 1 Success Criteria
- ✅ NASA GIBS displays daily imagery
- ✅ MapTiler shows high-res satellite globally
- ✅ Users can switch sources and adjust opacity
- ✅ Loads in <2 seconds at z=14
- ✅ Works in both deck.gl and MapLibre views

### Phase 2 Success Criteria
- ✅ Parker County visible at 0.6m resolution
- ✅ Seamless mosaic (no visible tile boundaries)
- ✅ Parcels/roads align with NAIP imagery
- ✅ Zoom-based layer switching works smoothly

### Phase 3 Success Criteria
- ✅ TiTiler serves tiles in <500ms
- ✅ Can update COG sources without code changes
- ✅ Band combinations work correctly
- ✅ Infrastructure costs <$50/month at scale

---

## Next Steps

### Immediate Actions

1. **Decision:** Review this plan and decide on Phase 1 approach (start with both GIBS + MapTiler recommended)

2. **Setup:**
   - Get MapTiler API key (free tier)
   - Configure secrets management for API key

3. **Quick Prototype:**
   - Add GIBS layer to MapLibreMap.razor
   - Test tile loading performance
   - Validate with your Parker County center point

4. **Phase 2 Prep:**
   - Research specific NAIP tiles for Parker County (FIPS 48367)
   - Estimate tile count and storage needs
   - Decide on tiling approach

### Questions to Resolve

1. **Budget:** What's acceptable monthly cost for satellite imagery in production?
2. **Update Frequency:** How often do you need new imagery? (affects NAIP vs commercial choice)
3. **Coverage:** Just Parker County, or expand to broader Texas/US?
4. **Infrastructure:** Preference for cloud provider (AWS, Azure, GCP)?
5. **Analytics:** Do you need multi-spectral data (NIR band) for analysis?

---

## Resources

### Documentation
- **NASA GIBS:** https://nasa-gibs.github.io/gibs-api-docs/
- **MapTiler Docs:** https://docs.maptiler.com/
- **NAIP on AWS:** https://registry.opendata.aws/naip/
- **TiTiler:** https://developmentseed.org/titiler/
- **COG Spec:** https://www.cogeo.org/

### Tools
- **GDAL:** https://gdal.org/
- **rasterio:** https://rasterio.readthedocs.io/
- **cogeo-mosaic:** https://developmentseed.org/cogeo-mosaic/
- **geotiff.js:** https://geotiffjs.github.io/

### Example Projects
- **Kyle Barron's NAIP Viewer:** https://github.com/kylebarron/naip-cogeo-mosaic
- **TiTiler CDK:** https://github.com/developmentseed/titiler/tree/main/deployment/aws
- **NASA VEDA:** https://github.com/NASA-IMPACT/veda-ui

---

## Appendix: Tile URL Reference

### Ready-to-Use Tile URLs

#### NASA GIBS - VIIRS True Color (Daily, 500m)
```
https://gibs.earthdata.nasa.gov/wmts/epsg3857/best/VIIRS_NOAA20_CorrectedReflectance_TrueColor/default/{YYYY-MM-DD}/GoogleMapsCompatible_Level9/{z}/{y}/{x}.jpg
```

#### NASA GIBS - Landsat 8 True Color (Every 16 days, 15m)
```
https://gibs.earthdata.nasa.gov/wmts/epsg3857/best/LANDSAT_WELD_CorrectedReflectance_TrueColor_Global_Annual/default/{YYYY-01-01}/GoogleMapsCompatible_Level7/{z}/{y}/{x}.png
```

#### MapTiler Satellite (Global, 30cm-1m)
```
https://api.maptiler.com/tiles/satellite-v2/{z}/{x}/{y}.jpg?key={YOUR_API_KEY}
```

#### MapTiler Hybrid (Satellite + Labels)
```
https://api.maptiler.com/tiles/hybrid/{z}/{x}/{y}.jpg?key={YOUR_API_KEY}
```

---

**Document Version:** 1.0
**Last Updated:** January 24, 2025
**Next Review:** After Phase 1 implementation
