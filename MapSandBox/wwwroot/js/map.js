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
            case 'scatterplotlayer':
                layers.push(new deck.ScatterplotLayer({
                    id: config.id,
                    data: config.dataUrl,
                    ...getLayerProperties(config)
                }));
                break;
            case 'heatmaplayer':
                layers.push(new deck.HeatmapLayer({
                    id: config.id,
                    data: config.dataUrl,
                    ...getLayerProperties(config)
                }));
                break;
            case 'pathlayer':
                layers.push(new deck.PathLayer({
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

    // Process function properties (@@= syntax)
    Object.keys(properties).forEach(key => {
        const value = properties[key];
        if (typeof value === 'string' && value.startsWith('@@=')) {
            properties[key] = createFunction(value.substring(3));
        }
    });

    // Handle specific layer configuration
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
        case 'noaa-rainfall-parker':
            // Ensure heatmap layer has proper configuration
            properties.pickable = false;
            properties.opacity = 0.8;
            break;
    }

    return properties;
}

function createFunction(functionBody) {
    // Handle different function patterns
    try {
        // Simple property accessors
        if (functionBody.startsWith('d.geometry.coordinates')) {
            return d => d.geometry.coordinates;
        }
        if (functionBody.startsWith('d.properties.rainfall')) {
            return d => d.properties.rainfall;
        }
        if (functionBody.includes('getCrashSeverityColor')) {
            return d => getCrashSeverityColor(d.properties.CrashSeverity);
        }
        if (functionBody.includes('getRiskLevelColor')) {
            return d => getRiskLevelColor(d.properties.RiskLevel);
        }

        // Array literals
        if (functionBody.startsWith('[[') && functionBody.endsWith(']]')) {
            return JSON.parse(functionBody);
        }

        // Default: create function from string
        return new Function('d', `return ${functionBody}`);
    } catch (error) {
        console.warn('Failed to create function from:', functionBody, error);
        return null;
    }
}

// Color helper functions for CRIS data
function getCrashSeverityColor(severity) {
    switch (severity) {
        case 'FATAL': return [255, 0, 0, 255];
        case 'SERIOUS': return [255, 165, 0, 255];
        case 'OTHER': return [255, 255, 0, 255];
        default: return [128, 128, 128, 255];
    }
}

function getRiskLevelColor(riskLevel) {
    switch (riskLevel) {
        case 'HIGH': return [255, 0, 0, 255];
        case 'MEDIUM': return [255, 165, 0, 255];
        case 'LOW': return [255, 255, 0, 255];
        default: return [128, 128, 128, 255];
    }
}

function loadDeckGL() {
    const script = document.createElement('script');
    script.src = 'https://unpkg.com/deck.gl@^9.1.14/dist.min.js';
    document.head.appendChild(script);
} 