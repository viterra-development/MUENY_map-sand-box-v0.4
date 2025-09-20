# Soil Data Viewport-Based Loading Optimization

**Status**: Future Enhancement
**Priority**: Medium
**Current State**: Loading full Parker County soil dataset (30MB) on map initialization
**Goal**: Load only soil features visible in current viewport to improve performance

## Current Implementation

- **Data Source**: Parker County SSURGO soil data via Azure CDN
- **Format**: GeoJSON (3 files: combined, clay, ksat visualization)
- **Size**: ~30MB per file, ~12,000 features total
- **Loading**: Full dataset loaded on map initialization
- **Performance**: Works but not optimal for mobile/slow connections

## Problem Statement

Deck.gl `GeoJsonLayer` loads entire GeoJSON files regardless of viewport, causing:
- Large initial download (30MB)
- Unnecessary memory usage for off-screen features
- Slower map initialization
- Poor mobile experience

## Standard Solutions

### Option A: Vector Tiles (Recommended - Industry Standard)

**Implementation**: Convert GeoJSON to Mapbox Vector Tiles (MVT)

```bash
# Generate vector tiles from soil data
tippecanoe -o parker-soil.mbtiles \
  --maximum-zoom=16 \
  --minimum-zoom=8 \
  --drop-densest-as-needed \
  parker-county-combined.geojson

# Upload tiles to Azure CDN
# Use deck.gl MVTLayer instead of GeoJsonLayer
```

**Benefits**:
- ✅ Automatic viewport-based loading
- ✅ 20-50KB per tile vs 30MB total
- ✅ Progressive loading by zoom level
- ✅ Industry standard format
- ✅ Native deck.gl support

**Code Changes**:
```javascript
// Replace GeoJsonLayer with MVTLayer
new MVTLayer({
  data: `${cdnUrl}/soil-tiles/{z}/{x}/{y}.pbf`,
  getFillColor: getSoilClayColor, // Same styling functions
  getLineColor: [139, 69, 19, 255],
  pickable: true
})
```

### Option B: Spatial API with Viewport Queries

**Implementation**: Backend service that returns features by bounding box

```javascript
// API endpoint
GET /api/soil/viewport?bbox=minX,minY,maxX,maxY&zoom=12&type=clay

// deck.gl integration
const soilLayer = new GeoJsonLayer({
  data: `${apiUrl}/soil/viewport?bbox=${bounds.join(',')}&zoom=${zoom}&type=clay`,
  updateTriggers: {
    data: [bounds, zoom] // Reload when viewport changes
  }
});
```

**Benefits**:
- ✅ Real-time viewport filtering
- ✅ Flexible querying capabilities
- ✅ Can include additional filters (soil type, clay %, etc.)

**Requirements**:
- Backend spatial service (PostGIS, SQL Server Spatial, etc.)
- Viewport change detection and debouncing
- API endpoint development

### Option C: Pre-computed Geographic Chunks

**Implementation**: Split Parker County into grid chunks, load visible chunks

```javascript
// Pre-process soil data into geographic grid
parker-county-soil/
├── chunk-0-0.geojson  // Northwest quadrant
├── chunk-0-1.geojson  // Northeast quadrant
├── chunk-1-0.geojson  // Southwest quadrant
└── chunk-1-1.geojson  // Southeast quadrant

// Load only visible chunks
const visibleChunks = getVisibleChunks(viewport, chunkSize);
const chunkPromises = visibleChunks.map(chunk =>
  fetch(`${cdnUrl}/soil-data/chunk-${chunk.x}-${chunk.y}.geojson`)
);
```

**Benefits**:
- ✅ Simpler than full tiling
- ✅ Works with existing CDN setup
- ✅ Reduces initial load significantly

### Option D: Level-of-Detail (LOD) Approach

**Implementation**: Multiple detail levels based on zoom

```javascript
// Simplified data for overview
parker-county-clay-lod0.geojson  // Zoom 1-12: 1,000 simplified features (~5MB)
parker-county-clay-lod1.geojson  // Zoom 13-16: 12,000 full features (~30MB)

// Load appropriate LOD based on zoom
const detailLevel = zoom < 13 ? 'lod0' : 'lod1';
const dataUrl = `${cdnUrl}/soil-data/parker-county-clay-${detailLevel}.geojson`;
```

## Implementation Roadmap

### Phase 1: Analysis & Planning
- [ ] Analyze current soil data usage patterns
- [ ] Benchmark current loading performance
- [ ] Choose optimization strategy based on requirements

### Phase 2: Vector Tiles (Recommended)
- [ ] Install `tippecanoe` for tile generation
- [ ] Update SoilDataProcessor to generate vector tiles
- [ ] Modify Azure upload to handle `.pbf` files
- [ ] Update MapLibre service to use `MVTLayer`
- [ ] Test viewport-based loading performance

### Phase 3: Alternative Implementation (if needed)
- [ ] Implement chosen alternative (Spatial API, Chunks, or LOD)
- [ ] Update frontend layer configuration
- [ ] Performance testing and optimization

## Technical Considerations

### Vector Tiles (MVT)
- **Tile Size**: Target 20-50KB per tile for optimal performance
- **Zoom Levels**: 8-16 for soil data (county to parcel level)
- **Simplification**: Use `--drop-densest-as-needed` for automatic optimization
- **Properties**: Ensure soil properties (clay%, ksat) are preserved in tiles

### Spatial API
- **Database**: Requires spatial database (PostGIS recommended)
- **Indexing**: Spatial indexes on soil polygon geometries
- **Caching**: Implement response caching for common viewport queries
- **Rate Limiting**: Prevent abuse of viewport-based queries

### Pre-computed Chunks
- **Grid Size**: Balance between number of chunks and chunk size
- **Overlap**: Consider slight overlap between chunks for seamless rendering
- **Metadata**: Track which chunks contain data vs empty areas

## Success Metrics

- **Initial Load Time**: Reduce from current ~30MB to <5MB
- **Viewport Change Performance**: <200ms to load new features
- **Memory Usage**: Reduce client-side memory footprint
- **Mobile Performance**: Improved experience on mobile devices
- **CDN Efficiency**: Better cache hit rates with smaller, discrete files

## Resources

- [Mapbox Vector Tile Specification](https://docs.mapbox.com/vector-tiles/specification/)
- [Tippecanoe Documentation](https://github.com/mapbox/tippecanoe)
- [Deck.gl MVTLayer](https://deck.gl/docs/api-reference/geo-layers/mvt-layer)
- [PostGIS Spatial Indexing](https://postgis.net/documentation/)

---

**Next Steps**: Evaluate current performance bottlenecks and user feedback to prioritize which optimization approach to implement first.