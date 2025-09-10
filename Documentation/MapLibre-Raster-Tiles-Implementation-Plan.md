# MapLibre Raster Tiles Implementation Plan

## Current Architecture Analysis

### Current Setup
- **Base Map**: MapLibre GL JS for vector tile rendering
- **Data Visualization**: deck.gl overlay using `MapboxOverlay` for interactive layers
- **Integration**: Hybrid approach where deck.gl layers are rendered on top of MapLibre base map

### Current Problem
We're trying to render PNG raster tiles (DEM data) through deck.gl's `TileLayer` + `BitmapLayer` combination, which is causing:
1. Undefined tile coordinates (`z/x/y` parameters)
2. BitmapLayer initialization errors (`count(): argument not a container`)
3. Unnecessary complexity for simple raster tile display

## Recommended Approach

### Option 1: Native MapLibre Raster Sources (RECOMMENDED)
Use MapLibre GL JS's built-in raster tile support, which is specifically designed for PNG/JPEG tile layers.

**Why this is better:**
- MapLibre GL JS has native, optimized support for raster tiles
- Automatic tile coordinate handling and caching
- Better performance (GPU-accelerated)
- Simpler implementation
- Consistent with how other map libraries handle raster tiles

**Implementation:**
```javascript
// Add raster source to MapLibre map
maplibreMap.addSource('parker-twi', {
    type: 'raster',
    tiles: ['https://mapsandbox-tiles-b0dfe8ffaga8d7ft.z03.azurefd.net/tiles/parker-twi/{z}/{x}/{y}.png'],
    tileSize: 256,
    minzoom: 8,
    maxzoom: 18
});

// Add raster layer
maplibreMap.addLayer({
    id: 'parker-twi',
    type: 'raster',
    source: 'parker-twi',
    paint: {
        'raster-opacity': 0.75
    }
});
```

### Option 2: deck.gl TileLayer (NOT RECOMMENDED for our use case)
Continue using deck.gl but fix the current implementation issues.

**Problems with this approach:**
- Over-engineered for simple raster tile display
- deck.gl TileLayer is more suited for data processing/visualization on tiles
- Additional complexity in coordinate handling
- Performance overhead of running through deck.gl rendering pipeline

## Implementation Plan

### Phase 1: Modify Layer Processing Logic

**Current Flow:**
```
C# LayerConfig → JavaScript createLayersFromConfig() → deck.gl TileLayer → BitmapLayer
```

**New Flow:**
```
C# LayerConfig → JavaScript createLayersFromConfig() → 
  ├── RasterTile → MapLibre addSource/addLayer
  └── Other layers → deck.gl layers
```

### Phase 2: Update JavaScript Integration

**File:** `maplibre-deckgl-integration.js`

**Changes needed:**

1. **Separate raster layers from deck.gl layers:**
```javascript
function createLayersFromConfig(layerConfigs, maplibreMap) {
    const deckLayers = [];
    const maplibreRasterLayers = [];
    
    layerConfigs.forEach(config => {
        if (!config.visible) return;
        
        if (config.type.toLowerCase() === 'rastertile') {
            maplibreRasterLayers.push(config);
        } else {
            // Process for deck.gl layers
            deckLayers.push(processForDeckGL(config));
        }
    });
    
    // Add MapLibre raster layers
    addMapLibreRasterLayers(maplibreMap, maplibreRasterLayers);
    
    return deckLayers;
}
```

2. **Add MapLibre raster layer management:**
```javascript
function addMapLibreRasterLayers(map, rasterLayers) {
    rasterLayers.forEach(config => {
        const sourceId = config.id;
        const layerId = config.id;
        
        // Add source if it doesn't exist
        if (!map.getSource(sourceId)) {
            map.addSource(sourceId, {
                type: 'raster',
                tiles: [config.dataUrl],
                tileSize: config.properties.tileSize || 256,
                minzoom: config.properties.minZoom || 0,
                maxzoom: config.properties.maxZoom || 18
            });
        }
        
        // Add layer if it doesn't exist
        if (!map.getLayer(layerId)) {
            map.addLayer({
                id: layerId,
                type: 'raster',
                source: sourceId,
                paint: {
                    'raster-opacity': config.properties.opacity || 0.75
                }
            });
        }
    });
}
```

3. **Update layer visibility handling:**
```javascript
function updateMapLibreRasterLayerVisibility(map, layerId, visible) {
    if (map.getLayer(layerId)) {
        map.setLayoutProperty(layerId, 'visibility', visible ? 'visible' : 'none');
    }
}
```

### Phase 3: Update Main Integration Functions

**Modify these functions:**
1. `createIntegratedMap()` - Pass maplibreMap to createLayersFromConfig
2. `updateIntegratedMapLayers()` - Handle both deck.gl and MapLibre layers
3. Add layer visibility toggle support for MapLibre layers

### Phase 4: Clean Up

**Remove/Simplify:**
1. `createRasterTileLayer()` function (no longer needed)
2. Complex BitmapLayer rendering logic
3. Custom tile coordinate handling

## Expected Benefits

### Performance Improvements
- Native GPU acceleration for raster tiles
- Efficient tile caching and loading
- Reduced JavaScript processing overhead

### Reliability Improvements
- No more undefined coordinate issues
- Proper tile coordinate handling by MapLibre
- Consistent behavior with other map libraries

### Code Simplicity
- Fewer lines of code
- More maintainable implementation
- Standard MapLibre patterns

## Implementation Steps

1. **Step 1**: Create `addMapLibreRasterLayers()` function
2. **Step 2**: Modify `createLayersFromConfig()` to separate raster layers
3. **Step 3**: Update `createIntegratedMap()` to handle both layer types
4. **Step 4**: Update `updateIntegratedMapLayers()` for layer visibility
5. **Step 5**: Test with parker-twi layer
6. **Step 6**: Remove old `createRasterTileLayer()` function
7. **Step 7**: Test all DEM layers (parker-elevation, parker-slope, etc.)

## Risk Assessment

**Low Risk Changes:**
- Adding new MapLibre layer functions alongside existing code
- Can be implemented incrementally

**Testing Strategy:**
- Keep old implementation as fallback during development
- Test each DEM layer individually
- Verify layer visibility toggles work correctly
- Confirm no impact on existing deck.gl layers (roads, parcels, etc.)

## Documentation References

- [MapLibre GL JS Raster Sources](https://maplibre.org/maplibre-gl-js-docs/style-spec/sources/#raster)
- [MapLibre GL JS Raster Layers](https://maplibre.org/maplibre-gl-js-docs/style-spec/layers/#raster)
- [deck.gl MapboxOverlay Integration](https://deck.gl/docs/api-reference/mapbox/mapbox-overlay)