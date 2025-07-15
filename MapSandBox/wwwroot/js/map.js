// Map.js - Deck.gl integration for Blazor
let deckgl = null;

export function createMap(containerId, config) {
    // Load deck.gl if not already loaded
    if (typeof deck === 'undefined') {
        loadDeckGL();
    }
    
    // Create layers from config
    const layers = createLayersFromConfig(config.layers);
    
    // Initialize deck.gl
    deckgl = new deck.DeckGL({
        container: containerId,
        initialViewState: {
            latitude: config.latitude,
            longitude: config.longitude,
            zoom: config.zoom,
            bearing: config.bearing,
            pitch: config.pitch
        },
        controller: true,
        layers: layers
    });
    
    return deckgl;
}

export function updateMapLayers(mapInstance, layers) {
    console.log('updateMapLayers called with:', layers);
    if (!mapInstance) {
        console.warn('No map instance available');
        return;
    }
    
    const deckLayers = createLayersFromConfig(layers);
    console.log('Created deck layers:', deckLayers);
    mapInstance.setProps({ layers: deckLayers });
}

export function disposeMap(mapInstance) {
    if (mapInstance) {
        mapInstance.finalize();
    }
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
            // Ensure all required properties for countries layer
            properties.stroked = true;
            properties.filled = false;
            properties.lineWidthMinPixels = 3;
            properties.opacity = 1.0;
            properties.getLineColor = [60, 60, 60];
            properties.getFillColor = [200, 200, 200];
            break;
        case 'rivers':
            // Ensure all required properties for rivers layer
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
    }
    
    return properties;
}

function loadDeckGL() {
    const script = document.createElement('script');
    script.src = 'https://unpkg.com/deck.gl@^9.0.0-beta.2/dist.min.js';
    document.head.appendChild(script);
} 