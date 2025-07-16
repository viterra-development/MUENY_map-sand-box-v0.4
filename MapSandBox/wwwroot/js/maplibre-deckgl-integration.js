// MapLibre + deck.gl integration for Blazor
let integratedMapInstance = null;
let maplibreMap = null;
let deckOverlay = null;

export function createIntegratedMap(containerId, config) {
    console.log('Creating integrated MapLibre + deck.gl map with config:', config);
    
    // Initialize MapLibre base map
    maplibreMap = new maplibregl.Map({
        container: containerId,
        style: config.baseMap?.style || 'https://basemaps.cartocdn.com/gl/positron-gl-style/style.json',
        center: [config.longitude, config.latitude],
        zoom: config.zoom,
        bearing: config.bearing || 0,
        pitch: config.pitch || 0,
        antialias: true
    });

    // Add navigation controls if enabled
    if (config.baseMap?.showControls !== false) {
        maplibreMap.addControl(new maplibregl.NavigationControl());
    }

    // Wait for map to load before adding deck.gl overlay
    maplibreMap.on('load', () => {
        // Create deck.gl layers from config
        const layers = createLayersFromConfig(config.layers);
        
        // Create deck.gl overlay using MapboxOverlay (works with MapLibre)
        deckOverlay = new deck.MapboxOverlay({
            interleaved: false, // Overlaid mode for better compatibility
            layers: layers
        });

        // Add deck.gl overlay to MapLibre map
        maplibreMap.addControl(deckOverlay);
        
        // Update the instance reference with the overlay
        integratedMapInstance.deckOverlay = deckOverlay;
        
        console.log('MapLibre + deck.gl integration complete');
    });

    // Store reference for later use (deckOverlay will be added after map loads)
    integratedMapInstance = {
        maplibre: maplibreMap,
        deckOverlay: null // Will be set after map loads
    };

    return integratedMapInstance;
}

export function updateIntegratedMapLayers(mapInstance, layers) {
    console.log('updateIntegratedMapLayers called with:', layers);
    if (!mapInstance || !mapInstance.deckOverlay) {
        console.warn('No integrated map instance available');
        return;
    }
    
    const deckLayers = createLayersFromConfig(layers);
    console.log('Created deck layers:', deckLayers);
    mapInstance.deckOverlay.setProps({ layers: deckLayers });
}

export function updateBaseMapStyle(mapInstance, styleUrl) {
    if (mapInstance && mapInstance.maplibre && styleUrl) {
        mapInstance.maplibre.setStyle(styleUrl);
    }
}

export function disposeIntegratedMap(mapInstance) {
    if (mapInstance) {
        if (mapInstance.deckOverlay) {
            // Remove deck.gl overlay
            mapInstance.maplibre.removeControl(mapInstance.deckOverlay);
        }
        if (mapInstance.maplibre) {
            mapInstance.maplibre.remove();
        }
    }
    integratedMapInstance = null;
    maplibreMap = null;
    deckOverlay = null;
}

function createLayersFromConfig(layerConfigs) {
    const layers = [];
    
    layerConfigs.forEach(config => {
        if (!config.visible) return;
        
        switch (config.type.toLowerCase()) {
            case 'geojson':
                layers.push(new deck.GeoJsonLayer({
                    id: config.id,
                    data: config.dataUrl,
                    ...getLayerProperties(config)
                }));
                break;
            case 'greatcircle':
                layers.push(new deck.GreatCircleLayer({
                    id: config.id,
                    data: config.dataUrl,
                    ...getLayerProperties(config)
                }));
                break;
            case 'arc':
                layers.push(new deck.ArcLayer({
                    id: config.id,
                    data: config.dataUrl,
                    ...getLayerProperties(config)
                }));
                break;
        }
    });
    
    return layers;
}

function getLayerProperties(config) {
    const properties = { ...config.properties };
    
    // Handle function properties based on layer type
    switch (config.id) {
        case 'countries':
            properties.stroked = true;
            properties.filled = false;
            properties.lineWidthMinPixels = 3;
            properties.opacity = 1.0;
            properties.getLineColor = [60, 60, 60];
            properties.getFillColor = [200, 200, 200];
            break;
        case 'rivers':
            properties.stroked = true;
            properties.filled = false;
            properties.lineWidthMinPixels = 1;
            properties.opacity = 0.6;
            properties.getLineColor = [100, 150, 255];
            properties.getFillColor = [100, 150, 255];
            break;
        case 'airports':
            properties.getPointRadius = f => (11 - f.properties.scalerank);
            properties.onClick = info => info.object && alert(`${info.object.properties.name} (${info.object.properties.abbrev})`);
            break;
        case 'flight-paths':
            properties.dataTransform = d => d.features.filter(f => f.properties.scalerank < 4);
            properties.getSourcePosition = f => [-0.4531566, 51.4709959]; // London
            properties.getTargetPosition = f => f.geometry.coordinates;
            break;
        case 'parker-roads':
            properties.stroked = true;
            properties.filled = false;
            properties.lineWidthMinPixels = 1;
            properties.lineWidthMaxPixels = 4;
            properties.opacity = 0.8;
            properties.getLineColor = getRoadColor;
            properties.getLineWidth = getRoadWidth;
            properties.pickable = true;
            properties.onClick = handleRoadClick;
            break;
    }
    
    return properties;
}

// Road styling functions
function getRoadColor(feature) {
    const rttyp = feature.properties.RTTYP;
    switch(rttyp) {
        case 'I': return [255, 0, 0];    // Red for Interstate
        case 'U': return [0, 0, 255];    // Blue for US Highway
        case 'S': return [0, 128, 0];    // Green for State Highway
        case 'M': return [128, 128, 128]; // Gray for Minor roads
        default: return [100, 100, 100]; // Default gray for other roads
    }
}

function getRoadWidth(feature) {
    const rttyp = feature.properties.RTTYP;
    switch(rttyp) {
        case 'I': return 4; // Interstate
        case 'U': return 3; // US Highway
        case 'S': return 2; // State Highway
        case 'M': return 1; // Minor roads
        default: return 1;  // Default width
    }
}

function handleRoadClick(info) {
    if (info.object) {
        const road = info.object;
        const name = road.properties.FULLNAME || 'Unnamed Road';
        const type = road.properties.RTTYP || 'Unknown';
        const roadType = getRoadTypeName(type);
        
        alert(`Road: ${name}\nType: ${roadType}\nRoute Type Code: ${type}`);
    }
}

function getRoadTypeName(rttyp) {
    switch(rttyp) {
        case 'I': return 'Interstate';
        case 'U': return 'US Highway';
        case 'S': return 'State Highway';
        case 'M': return 'Minor Road';
        default: return 'Other';
    }
}

// Export for debugging
export function getIntegratedMapInstance() {
    return integratedMapInstance;
}