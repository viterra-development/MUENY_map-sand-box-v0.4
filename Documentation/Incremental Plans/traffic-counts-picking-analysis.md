# Traffic Counts Layer Picking Issue Analysis

## Problem Description

The traffic counts layer using deck.gl TileLayer is not responding to click events (picking) because the data is consistently null in the `renderSubLayers` function.

## Root Cause Analysis

### 1. Missing Tile Endpoint
The TileLayer is configured to load data from `/tiles/traffic-counts/{z}/{x}/{y}.geojson` (line 254 in `maplibre-deckgl-integration.js`), but this endpoint returns HTTP 404:

```bash
curl -I "http://localhost:5214/tiles/traffic-counts/16/14991/26450.geojson"
# Returns: HTTP/1.1 404 Not Found
```

### 2. Null Data Prevents Picking
According to the [deck.gl picking documentation](https://deck.gl/docs/developer-guide/interactivity#what-can-be-picked):

> "The picking engine identifies which object in which layer is at the given coordinates. While usually intuitive, what constitutes a pickable 'object' is defined by each layer. Typically, it corresponds to one of the data entries that is passed in via prop.data."

Since `props.data` is null in our `renderSubLayers` function (as shown in console logs), there are no data entries to pick. The picking mechanism requires actual data objects to function.

### 3. Console Evidence
The logs clearly show the issue:
```javascript
TileLayer renderSubLayers called with props: {id: 'traffic-counts-14992-26448-16', data: null, ...}
Data: null
```

## Available Data Resources

The project contains actual traffic count data in GeoJSON format:
- `/workspaces/map-sand-box/MapSandBox/wwwroot/parker_county_traffic_locations_20250731_111008.geojson`

This file is accessible via the web server at `/parker_county_traffic_locations_20250731_111008.geojson`.

## Proposed Solution

Replace the TileLayer approach with a direct GeoJsonLayer that loads the available data file:

### Why TileLayer Failed:
1. **No tile server**: The application doesn't have a tile server endpoint for traffic counts data
2. **404 responses**: All tile requests return 404, resulting in null data
3. **No fallback**: The TileLayer has no mechanism to fall back to static data when tiles fail

### Why GeoJsonLayer Will Work:
1. **Direct data access**: Loads the actual available GeoJSON file
2. **Guaranteed data**: The file exists and contains valid GeoJSON features
3. **Picking compatibility**: According to deck.gl docs, GeoJsonLayer picking works with "a GeoJSON feature in the props.data feature collection"
4. **Event handling**: With real data objects, click events can access feature properties for tooltips/interactions

### deck.gl Documentation References:

**TileLayer limitations** ([TileLayer docs](https://deck.gl/docs/api-reference/geo-layers/tile-layer)):
- Requires a valid tile server endpoint
- Data loading failures result in null data passed to renderSubLayers

**GeoJsonLayer picking** ([GeoJsonLayer docs](https://deck.gl/docs/api-reference/layers/geojson-layer)):
- "an object is a GeoJSON feature in the props.data feature collection"
- Supports onClick events with access to the clicked feature's properties

**General picking requirements** ([Interactivity docs](https://deck.gl/docs/developer-guide/interactivity)):
- "what constitutes a pickable 'object' is defined by each layer"
- Objects must exist in the data to be pickable

## Implementation Change

Convert from:
```javascript
new deck.TileLayer({
    data: '/tiles/traffic-counts/{z}/{x}/{y}.geojson', // 404 endpoint
    renderSubLayers: props => new deck.GeoJsonLayer({...props}) // props.data is null
})
```

To:
```javascript
new deck.GeoJsonLayer({
    data: '/parker_county_traffic_locations_20250731_111008.geojson', // Existing file
    pickable: true,
    onClick: (info) => { /* info.object contains GeoJSON feature */ }
})
```

This ensures the layer has actual data objects for the picking engine to work with.